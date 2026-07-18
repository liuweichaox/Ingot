// Central API（中心侧）：提供中心 API（边缘注册/心跳、诊断代理、查询与管理）。

using Ingot.Agent;
using Ingot.Agent.Infrastructure;
using Ingot.Central.Api.Agents;
using Ingot.Central.Api.HealthChecks;
using Ingot.Central.Api.Events;
using Ingot.Central.Api.Inspections;
using Ingot.Central.Api.Configuration;
using Ingot.Central.Infrastructure;
using Ingot.Central.Infrastructure.HealthChecks;
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

builder.Services.AddIngotCentralInfrastructure(builder.Configuration);
builder.Services.AddIngotAgentCore(builder.Configuration);
builder.Services.AddIngotAgentInfrastructure(builder.Configuration);

// 宿主职责：入站鉴权策略
builder.Services.AddSingleton<EdgeTokenValidator>();
builder.Services.AddSingleton<InspectionActorTokenValidator>();
builder.Services.AddSingleton<AgentTokenValidator>();
builder.Services.AddSingleton<ChatTokenValidator>();

builder.Services.AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite")
    .AddCheck<PostgresEventStoreHealthCheck>("event-store");

// CORS：给 Vite 开发服务器或独立静态站点调用 API。
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        // 支持配置：Cors:AllowedOrigins=["http://localhost:3000", "..."]
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0)
        {
            policy.WithOrigins("http://localhost:3000")
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
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
    service = "Ingot.Central.Api",
    endpoints = new
    {
        edges = "/api/edges",
        metrics = "/metrics",
        metricsJson = "/api/metrics-data",
        events = "/api/v1/events",
        eventStream = "/api/v1/events/stream",
        eventIngest = "/api/v1/events:batch",
        inspectionRecords = "/api/v1/inspection-records",
        subscriptions = "/api/v1/subscriptions",
        chatRuns = "/api/v1/chat/runs",
        chatCapabilities = "/api/v1/chat/capabilities",
        agentRuns = "/api/v1/agent/runs",
        agentCapabilities = "/api/v1/agent/capabilities",
        agentArtifacts = "/api/v1/agent/artifacts",
        connectorWorkspaces = "/api/v1/connector-workspaces/{workspaceId}"
    }
}));

// 解析并显示所有监听地址
var addresses = urls.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var baseAddress = addresses.First();

Log.Logger.Information("==================================================================");
Log.Logger.Information("              Central API Service Started");
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
Log.Logger.Information("    > Inspections:   {0}/api/v1/inspection-records", baseAddress);
Log.Logger.Information("    > Subscriptions: {0}/api/v1/subscriptions", baseAddress);
Log.Logger.Information("    > Chat Runs:     {0}/api/v1/chat/runs", baseAddress);
Log.Logger.Information("    > Chat Capabilities:{0}/api/v1/chat/capabilities", baseAddress);
Log.Logger.Information("    > Desktop Agent Runs:{0}/api/v1/agent/runs", baseAddress);
Log.Logger.Information("    > Desktop Agent Capabilities:{0}/api/v1/agent/capabilities", baseAddress);
Log.Logger.Information("    > Agent Artifacts:{0}/api/v1/agent/artifacts", baseAddress);
Log.Logger.Information("    > Connector Workspaces:{0}/api/v1/connector-workspaces/{{workspaceId}}", baseAddress);
Log.Logger.Information("==================================================================");

app.Run();
