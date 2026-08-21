
using System.Diagnostics;
using Ingot.Agent;
using Ingot.Agent.Providers;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Configuration;
using Ingot.Platform.Api.Errors;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Api.HealthChecks;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Identity;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
    ProductionConfigurationValidator.Validate(builder.Configuration);

var urls = builder.Configuration["Urls"]
    ?? throw new InvalidOperationException("Urls is required.");
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("edge-diagnostics")
    .ConfigureHttpClient(static client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {

        AllowAutoRedirect = false
    });
builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new ApiProblemDetailsConvention());
});
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var status = context.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError;
        context.ProblemDetails.Type ??= $"urn:ingot:problem:{ApiProblemDetailsFactory.CodeFor(status)}";
        context.ProblemDetails.Title ??= ReasonPhrases.GetReasonPhrase(status);
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
        context.ProblemDetails.Extensions["code"] = ApiProblemDetailsFactory.CodeFor(status);
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    };
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = ApiProblemDetailsFactory.Create(
            context.HttpContext,
            StatusCodes.Status400BadRequest,
            "请求字段验证失败。");
        problem.Extensions["errors"] = context.ModelState
            .Where(static pair => pair.Value?.Errors.Count > 0)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "字段值无效。"
                        : error.ErrorMessage).ToArray(),
                StringComparer.Ordinal);
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});
builder.Services.AddOpenApi();

var authenticationMode = builder.Configuration["Authentication:Mode"] ?? "Local";
var useAnonymousDevelopmentIdentity = builder.Environment.IsDevelopment()
    || string.Equals(authenticationMode, "Disabled", StringComparison.OrdinalIgnoreCase);
var useOidc = !useAnonymousDevelopmentIdentity
    && string.Equals(authenticationMode, "Oidc", StringComparison.OrdinalIgnoreCase);
if (useAnonymousDevelopmentIdentity)
{
    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            static _ => { });
}
else if (useOidc)
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Authentication:Authority"];
            options.Audience = builder.Configuration["Authentication:Audience"];
            options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
            options.MapInboundClaims = true;
        });
}
else
{
    builder.Services.AddAuthentication(LocalTokenAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, LocalTokenAuthenticationHandler>(
            LocalTokenAuthenticationHandler.SchemeName,
            static _ => { });
}
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(
            PlatformRoles.QualityInspector,
            PlatformRoles.QualityReviewer,
            PlatformRoles.ProcessEngineer,
            PlatformRoles.PlatformAdministrator)
        .Build();
});

builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotInspectionInfrastructure(builder.Configuration);

builder.Services.AddIngotLocalIdentity(builder.Configuration);
builder.Services.AddIngotAgentCore(builder.Configuration);
builder.Services.AddIngotAgentProviders(builder.Configuration);

builder.Services.AddSingleton<EdgeTokenValidator>();
builder.Services.Configure<EdgeDiagnosticsOptions>(builder.Configuration.GetSection("EdgeDiagnostics"));
builder.Services.AddSingleton<EdgeDiagnosticsTokenProvider>();
builder.Services.AddSingleton<PlatformUserResolver>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresPlatformEventStoreHealthCheck>("event-store");

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {

        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        string[] allowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];
        if (origins.Length == 0)
        {
            policy.WithOrigins("http://localhost:3000")
                .AllowAnyHeader()
                .WithMethods(allowedMethods);
        }
        else
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .WithMethods(allowedMethods);
        }
    });
});

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment() &&
    string.Equals(authenticationMode, "Disabled", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogCritical(
        "INSECURE DEMO AUTHENTICATION IS ENABLED IN PRODUCTION. " +
        "All authenticated requests use the fixed development identity; isolate this deployment.");
}

app.UseRouting();

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

app.UseHttpMetrics();

app.MapMetrics("/metrics").AllowAnonymous();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

app.MapControllers();

try
{
    var eventStore = app.Services.GetRequiredService<IPlatformEventStore>();
    var canConnect = await eventStore.CanConnectAsync(CancellationToken.None).ConfigureAwait(false);
    if (!canConnect)
    {
        app.Logger.LogCritical(
            "Platform.Api 启动失败：无法连接到 PostgreSQL 事件存储。" +
            "请检查数据库连接字符串和 PostgreSQL 服务状态。");
        Environment.Exit(1);
    }
    app.Logger.LogInformation("Platform.Api 依赖检查: PostgreSQL 事件存储就绪 ✓");
}
catch (Exception ex)
{
    app.Logger.LogCritical(ex,
        "Platform.Api 启动失败：初始化事件存储时发生异常。");
    Environment.Exit(1);
}

app.MapGet("/", () => Results.Ok(new
{
    service = "Ingot.Platform.Api",
    endpoints = new
    {
        edges = "/api/edges",
        metrics = "/metrics",
        metricsJson = "/api/metrics-data",
        events = "/api/v1/events",
        eventStream = "/api/v1/events/stream",
        eventIngest = "/api/v1/events:batch",
        attachments = "/api/v1/inspection-attachments",
        inspectionDefinitions = "/api/v1/inspection-definitions",
        inspectionPlans = "/api/v1/inspection-plans",
        inspectionRecords = "/api/v1/inspection-records",
        inspectionTasks = "/api/v1/inspection-tasks",
        inspectionReviews = "/api/v1/inspection-reviews",
        executions = "/api/v1/process-executions",
        executionComparisons = "/api/v1/execution-comparisons/{executionId}",
        timeWindowComparisons = "/api/v1/time-window-comparisons",
        executionAnalysisBackfills = "/api/v1/process-execution-analysis-backfills",
        executionFeatureAggregates = "/api/v1/process-feature-aggregates",
        scenarioPackages = "/api/v1/scenario-packages",
        goldenQuestions = "/api/v1/golden-questions",
        processModels = "/api/v1/process-models",
        trainingDatasets = "/api/v1/training-datasets",
        processKnowledge = "/api/v1/process-knowledge",
        researchProjects = "/api/v1/research-projects",
        researchAssets = "/api/v1/dataset-quality-validations",
        toolingTypes = "/api/v1/tooling-types",
        toolingComponents = "/api/v1/tooling-components",
        toolingAssemblies = "/api/v1/tooling-assemblies",
        toolingInstallations = "/api/v1/tooling-installations",
        productionContexts = "/api/v1/production-contexts",
        auth = "/api/v1/auth/login",
        users = "/api/v1/users",
        chatRuns = "/api/v1/chat/runs",
        chatCapabilities = "/api/v1/chat/capabilities"
    }
})).AllowAnonymous();

var addresses = urls.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var baseAddress = addresses.First();

Log.Logger.Information("==================================================================");
Log.Logger.Information("              Platform API Service Started");
Log.Logger.Information("==================================================================");
Log.Logger.Information("  Service Addresses:");
foreach (var addr in addresses)
{
    Log.Logger.Information("    > {0}", addr.Trim());
}
Log.Logger.Information("==================================================================");
Log.Logger.Information("  Endpoints:");
Log.Logger.Information("    > Health Check:  {0}/health", baseAddress);
Log.Logger.Information("    > Edge List:     {0}/api/edges", baseAddress);
Log.Logger.Information("    > Metrics:       {0}/metrics", baseAddress);
Log.Logger.Information("    > Metrics JSON:  {0}/api/metrics-data", baseAddress);
Log.Logger.Information("    > Edge Metrics:  {0}/api/edges/{{edgeId}}/metrics/json", baseAddress);
Log.Logger.Information("    > Edge Logs:     {0}/api/edges/{{edgeId}}/logs", baseAddress);
Log.Logger.Information("    > Events:        {0}/api/v1/events", baseAddress);
Log.Logger.Information("    > Event Stream:  {0}/api/v1/events/stream", baseAddress);
Log.Logger.Information("    > Event Ingest:  {0}/api/v1/events:batch", baseAddress);
Log.Logger.Information("    > Attachments:      {0}/api/v1/inspection-attachments", baseAddress);
Log.Logger.Information("    > Definitions:   {0}/api/v1/inspection-definitions", baseAddress);
Log.Logger.Information("    > Quality Plans: {0}/api/v1/inspection-plans", baseAddress);
Log.Logger.Information("    > Inspections:   {0}/api/v1/inspection-records", baseAddress);
Log.Logger.Information("    > Quality Tasks: {0}/api/v1/inspection-tasks", baseAddress);
Log.Logger.Information("    > Reviews:       {0}/api/v1/inspection-reviews", baseAddress);
Log.Logger.Information("    > ProcessExecutions:        {0}/api/v1/process-executions", baseAddress);
Log.Logger.Information("    > Comparisons:   {0}/api/v1/execution-comparisons/{{executionId}}", baseAddress);
Log.Logger.Information("    > Research:      {0}/api/v1/research-projects", baseAddress);
Log.Logger.Information("    > Models:        {0}/api/v1/process-models", baseAddress);
Log.Logger.Information("    > Knowledge:     {0}/api/v1/process-knowledge", baseAddress);
Log.Logger.Information("    > Dataset Quality:{0}/api/v1/dataset-quality-validations", baseAddress);
Log.Logger.Information("    > Tooling Types: {0}/api/v1/tooling-types", baseAddress);
Log.Logger.Information("    > Components:    {0}/api/v1/tooling-components", baseAddress);
Log.Logger.Information("    > Assemblies:    {0}/api/v1/tooling-assemblies", baseAddress);
Log.Logger.Information("    > Installations: {0}/api/v1/tooling-installations", baseAddress);
Log.Logger.Information("    > Prod Contexts: {0}/api/v1/production-contexts", baseAddress);
Log.Logger.Information("    > Chat Runs:     {0}/api/v1/chat/runs", baseAddress);
Log.Logger.Information("    > Chat Capabilities:{0}/api/v1/chat/capabilities", baseAddress);
Log.Logger.Information("==================================================================");

app.Run();

public partial class Program;
