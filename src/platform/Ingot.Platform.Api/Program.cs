// Platform API（中心侧）：提供中心 API（边缘注册/心跳、诊断代理、查询与管理）。

using Ingot.Agent;
using Ingot.Agent.Providers;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.HealthChecks;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Api.Inspections;
using Ingot.Platform.Api.Configuration;
using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.HealthChecks;
using Serilog;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
    ProductionConfigurationValidator.Validate(builder.Configuration);

var urls = builder.Configuration["Urls"]
    ?? throw new InvalidOperationException("Urls is required.");
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();
builder.Services.AddControllers();

builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotAgentCore(builder.Configuration);
builder.Services.AddIngotAgentProviders(builder.Configuration);

// 宿主职责：入站鉴权策略
builder.Services.AddSingleton<EdgeTokenValidator>();
builder.Services.AddSingleton<InspectionActorTokenValidator>();
builder.Services.AddSingleton<ChatTokenValidator>();

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
app.UseAuthorization();

app.UseCors("frontend");

// Prometheus 指标（中心自身进程）
app.UseHttpMetrics();
// Prometheus 原始指标（官方默认端点）
app.MapMetrics("/metrics");

// Health checks（官方风格）：统一用 /health
app.MapHealthChecks("/health");

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
        evidence = "/api/v1/evidence",
        inspectionDefinitions = "/api/v1/inspection-definitions",
        inspectionRecords = "/api/v1/inspection-records",
        subscriptions = "/api/v1/subscriptions",
        chatRuns = "/api/v1/chat/runs",
        chatCapabilities = "/api/v1/chat/capabilities"
    }
}));

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
Log.Logger.Information("    > Evidence:      {0}/api/v1/evidence", baseAddress);
Log.Logger.Information("    > Definitions:   {0}/api/v1/inspection-definitions", baseAddress);
Log.Logger.Information("    > Inspections:   {0}/api/v1/inspection-records", baseAddress);
Log.Logger.Information("    > Subscriptions: {0}/api/v1/subscriptions", baseAddress);
Log.Logger.Information("    > Chat Runs:     {0}/api/v1/chat/runs", baseAddress);
Log.Logger.Information("    > Chat Capabilities:{0}/api/v1/chat/capabilities", baseAddress);
Log.Logger.Information("==================================================================");

app.Run();
