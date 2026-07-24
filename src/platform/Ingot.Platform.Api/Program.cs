// Platform API（中心侧）：提供中心 API（边缘注册/心跳、诊断代理、查询与管理）。

using Ingot.Agent;
using Ingot.Agent.Providers;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.HealthChecks;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Api.Configuration;
using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.HealthChecks;
using Ingot.Platform.Infrastructure.Identity;
using Serilog;
using Prometheus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
    ProductionConfigurationValidator.Validate(builder.Configuration);

var urls = builder.Configuration["Urls"]
    ?? throw new InvalidOperationException("Urls is required.");
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();
builder.Services.AddControllers();

// 三种认证模式：
//   开发环境 → 固定本地身份（不引入第二套登录）；
//   生产 + Authentication:Mode=Oidc → 外部 JWT 颁发者；
//   生产 + Authentication:Mode=Local（默认）→ 内置本地账户会话令牌，消除强制 OIDC 依赖。
var useOidc = !builder.Environment.IsDevelopment()
    && string.Equals(builder.Configuration["Authentication:Mode"], "Oidc", StringComparison.OrdinalIgnoreCase);
if (builder.Environment.IsDevelopment())
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
        .Build();
});

builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
// 本地账户服务在基础设施（含迁移）之后注册，保证播种晚于建表；播种服务自身仅在 Local 模式生效。
builder.Services.AddIngotLocalIdentity(builder.Configuration);
builder.Services.AddIngotAgentCore(builder.Configuration);
builder.Services.AddIngotAgentProviders(builder.Configuration);

// 宿主职责：入站鉴权策略
builder.Services.AddSingleton<EdgeTokenValidator>();
builder.Services.AddSingleton<PlatformUserResolver>();

builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite")
    .AddCheck<PostgresPlatformEventStoreHealthCheck>("event-store");

// CORS：给 Vite 开发服务器或独立静态站点调用 API。
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        // 支持配置：Cors:AllowedOrigins=["http://localhost:3000", "..."]
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        // 仅暴露本 API 实际使用的方法（查询/SSE 用 GET，摄入/取消用 POST），收敛 CORS 面。
        // 头部保持放开，因为需要 Authorization、Content-Type 与 SSE 续读的 Last-Event-ID。
        string[] allowedMethods = ["GET", "POST", "DELETE", "OPTIONS"];
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

app.UseRouting();
// CORS 必须位于 UseRouting 之后、UseAuthentication/UseAuthorization 之前（官方规定顺序）；
// 否则 401/403 响应不携带 CORS 头，前端只能看到不明网络错误。
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

// Prometheus 指标（中心自身进程）
app.UseHttpMetrics();
// Prometheus 原始指标（官方默认端点）
app.MapMetrics("/metrics").AllowAnonymous();

// Health checks（官方风格）：统一用 /health
app.MapHealthChecks("/health").AllowAnonymous();

// Attribute routing（/api/..）
app.MapControllers();

// 方便验证服务是否启动（不提供页面）
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
        cycles = "/api/v1/cycles",
        problemCases = "/api/v1/problem-cases",
        cycleComparisons = "/api/v1/cycle-comparisons/{correlationId}",
        processWindowComparisons = "/api/v1/process-window-comparisons",
        cycleAnalysisBackfills = "/api/v1/cycle-analysis-backfills",
        cycleFeatureAggregates = "/api/v1/cycle-feature-aggregates",
        processInvestigations = "/api/v1/process-investigations",
        processModels = "/api/v1/process-models",
        trainingDatasets = "/api/v1/training-datasets",
        processKnowledge = "/api/v1/process-knowledge",
        parameterRecommendations = "/api/v1/parameter-recommendations",
        toolingTypes = "/api/v1/tooling-types",
        toolingComponents = "/api/v1/tooling-components",
        toolingAssemblies = "/api/v1/tooling-assemblies",
        toolingInstallations = "/api/v1/tooling-installations",
        productionContexts = "/api/v1/production-contexts",
        subscriptions = "/api/v1/subscriptions",
        auth = "/api/v1/auth/login",
        users = "/api/v1/users",
        chatRuns = "/api/v1/chat/runs",
        chatCapabilities = "/api/v1/chat/capabilities"
    }
})).AllowAnonymous();

// 解析并显示所有监听地址
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
Log.Logger.Information("    > Cycles:        {0}/api/v1/cycles", baseAddress);
Log.Logger.Information("    > Comparisons:   {0}/api/v1/cycle-comparisons/{{correlationId}}", baseAddress);
Log.Logger.Information("    > Improvement:   {0}/api/v1/process-investigations", baseAddress);
Log.Logger.Information("    > Models:        {0}/api/v1/process-models", baseAddress);
Log.Logger.Information("    > Knowledge:     {0}/api/v1/process-knowledge", baseAddress);
Log.Logger.Information("    > Recommendations:{0}/api/v1/parameter-recommendations", baseAddress);
Log.Logger.Information("    > Tooling Types: {0}/api/v1/tooling-types", baseAddress);
Log.Logger.Information("    > Components:    {0}/api/v1/tooling-components", baseAddress);
Log.Logger.Information("    > Assemblies:    {0}/api/v1/tooling-assemblies", baseAddress);
Log.Logger.Information("    > Installations: {0}/api/v1/tooling-installations", baseAddress);
Log.Logger.Information("    > Prod Contexts: {0}/api/v1/production-contexts", baseAddress);
Log.Logger.Information("    > Subscriptions: {0}/api/v1/subscriptions", baseAddress);
Log.Logger.Information("    > Chat Runs:     {0}/api/v1/chat/runs", baseAddress);
Log.Logger.Information("    > Chat Capabilities:{0}/api/v1/chat/capabilities", baseAddress);
Log.Logger.Information("==================================================================");

app.Run();
