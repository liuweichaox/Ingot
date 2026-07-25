// Connector Host：接收用户接入程序产生的标准事件并上报中心。

using System.Security.Cryptography;
using System.Text;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Ingot.Edge.Infrastructure.Events;
using Ingot.Edge.Infrastructure.Logs;
using Ingot.Edge.Infrastructure.Metrics;
using Ingot.Edge.Infrastructure.Reporting;
using Ingot.Edge.Infrastructure.State;
using Ingot.Edge.ConnectorHost.BackgroundServices;
using Ingot.Edge.ConnectorHost.HealthChecks;
using Ingot.Edge.ConnectorHost.Services;
using Ingot.Edge.ConnectorHost.Configuration;
using Ingot.Edge.ConnectorHost.Acquisition;
using Prometheus;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("DeviceSimulator"))
    ProductionConfigurationValidator.Validate(builder.Configuration);

var urls = builder.Configuration["Urls"]
    ?? throw new InvalidOperationException("Urls is required.");
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();

builder.Services.Configure<Ingot.Domain.Events.EventOptions>(builder.Configuration.GetSection("Events"));
builder.Services.Configure<LogOptions>(builder.Configuration.GetSection("Logging"));

// 配置 Edge 上报（注册/心跳）
builder.Services.Configure<EdgeReportingOptions>(builder.Configuration.GetSection("Edge"));
builder.Services.Configure<HttpPollingAcquisitionOptions>(builder.Configuration.GetSection("Acquisition"));
builder.Services.AddSingleton<EdgeIdentityService>();
builder.Services.AddSingleton<IEdgeIdentityProvider>(services => services.GetRequiredService<EdgeIdentityService>());
builder.Services.AddSingleton<IPlatformReportingClient, PlatformReportingClient>();

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddSingleton<MetricsBridge>();
builder.Services.AddSingleton<IEdgeContextStore, ContextStore>();
builder.Services.AddSingleton<IEventLog, SqliteEventLog>();
builder.Services.AddSingleton<IEventPersistenceHealth, EventPersistenceHealth>();
builder.Services.AddSingleton<IEventSink, EventSink>();
builder.Services.AddSingleton<IEventShipper, HttpEventShipper>();
builder.Services.AddSingleton<AcquisitionStatus>();
builder.Services.AddSingleton<IAcquisitionSecretResolver, EnvironmentAcquisitionSecretResolver>();
builder.Services.AddSingleton<IAcquisitionProtocolRunner, MqttAcquisitionRunner>();
builder.Services.AddSingleton<IAcquisitionProtocolRunner, OpcUaAcquisitionRunner>();
builder.Services.AddSingleton<IAcquisitionProtocolRunner, ModbusTcpAcquisitionRunner>();
builder.Services.AddSingleton<IAcquisitionProtocolRunner, MelsecA1EAcquisitionRunner>();

// 日志查看服务（使用 SQLite）
builder.Services.AddSingleton<ILogViewService, SqliteLogViewService>();

builder.Services.AddHostedService<EdgePlatformReporterHostedService>();
builder.Services.AddHostedService<EventShipperHostedService>();
builder.Services.AddHostedService<HttpPollingAcquisitionHostedService>();

builder.Services.AddControllers();

// Health checks（官方风格）：统一用 /health
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("ok"))
    .AddCheck<EventLogHealthCheck>("event-log", tags: ["ready"]);

// 配置 SQLite 日志数据库路径（从配置读取，支持相对路径和绝对路径）
var logOptions = new LogOptions();
builder.Configuration.GetSection("Logging").Bind(logOptions);

var logDbPath = logOptions.DatabasePath;
if (!Path.IsPathRooted(logDbPath)) logDbPath = Path.Combine(AppContext.BaseDirectory, logDbPath);
Directory.CreateDirectory(Path.GetDirectoryName(logDbPath)!);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate:
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Sink(new MicrosoftSqliteSink(
        logDbPath,
        batchSize: 100,
        flushInterval: TimeSpan.FromSeconds(2),
        retentionDays: logOptions.RetentionDays))
    .CreateLogger();
builder.Host.UseSerilog();

var app = builder.Build();

app.UseRouting();

// 本地查询 API（日志/事件/周期/指标）使用独立令牌 ConnectorHost:LocalApiToken，
// 与向平台上行的 Edge:EventIngestToken 分离——避免把"能看本地日志"的人
// 顺带授予"能向平台注入事件"的凭据（跨信任边界复用）。
// 未配置本地令牌时暂回退旧行为并告警，给现场留出轮换窗口。
var localApiToken = app.Configuration["ConnectorHost:LocalApiToken"];
if (string.IsNullOrWhiteSpace(localApiToken))
{
    localApiToken = app.Configuration["Edge:EventIngestToken"];
    Log.Warning(
        "ConnectorHost:LocalApiToken 未配置，本地查询 API 暂以上行令牌 Edge:EventIngestToken 保护；" +
        "建议尽快配置独立的本地令牌并轮换。");
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var protectedPath =
        path.StartsWithSegments("/api/logs") ||
        path.StartsWithSegments("/api/v1/acquisition") ||
        path.StartsWithSegments("/api/v1/events") ||
        path.StartsWithSegments("/api/v1/cycles") ||
        path.StartsWithSegments("/api/v1/context") ||
        path.StartsWithSegments("/metrics");
    if (!protectedPath)
    {
        await next(context).ConfigureAwait(false);
        return;
    }

    var expected = localApiToken;
    var authorization = context.Request.Headers.Authorization.ToString();
    var valid = !string.IsNullOrWhiteSpace(expected) &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(authorization["Bearer ".Length..].Trim()));
    if (!valid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "采集节点访问凭据无效。" }).ConfigureAwait(false);
        return;
    }
    await next(context).ConfigureAwait(false);
});

// 添加 Prometheus HTTP 指标收集
app.UseHttpMetrics();

// 初始化 System.Diagnostics.Metrics 到 Prometheus 的桥接
var metricsBridge = app.Services.GetRequiredService<MetricsBridge>();
metricsBridge.StartListening();

// 暴露 Prometheus 指标端点
app.MapMetrics();

app.MapControllers();
app.MapHealthChecks("/health");

// 方便验证服务是否启动（不提供页面）
app.MapGet("/", () => Results.Ok(new
{
    service = "Ingot.Edge.ConnectorHost",
    endpoints = new
    {
        health = "/health",
        metrics = "/metrics",
        logs = "/api/logs",
        logLevels = "/api/logs/levels",
        connectorEvents = "/api/v1/connector-events",
        acquisitionStatus = "/api/v1/acquisition/status",
        events = "/api/v1/events",
        eventStream = "/api/v1/events/stream",
        cycle = "/api/v1/cycles/{correlationId}",
        context = "/api/v1/context/{subjectType}/{subjectId}"
    }
}));

// 解析并显示所有监听地址
var addresses = urls.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var baseAddress = addresses.FirstOrDefault()?.Trim() ?? "http://localhost:8001";

Log.Logger.Information("==================================================================");
Log.Logger.Information("              Connector Host Service Started");
Log.Logger.Information("==================================================================");
Log.Logger.Information("  Service Addresses:");
foreach (var addr in addresses)
{
    Log.Logger.Information("    > {0}", addr.Trim());
}
Log.Logger.Information("==================================================================");
Log.Logger.Information("  Endpoints:");
Log.Logger.Information("    > Health Check:  {0}/health", baseAddress);
Log.Logger.Information("    > Metrics:       {0}/metrics", baseAddress);
Log.Logger.Information("    > Logs:          {0}/api/logs", baseAddress);
Log.Logger.Information("    > Log Levels:    {0}/api/logs/levels", baseAddress);
Log.Logger.Information("    > Events:        {0}/api/v1/events", baseAddress);
Log.Logger.Information("    > Event Stream:  {0}/api/v1/events/stream", baseAddress);
Log.Logger.Information("    > Context:       {0}/api/v1/context/{{subjectType}}/{{subjectId}}", baseAddress);
Log.Logger.Information("==================================================================");

await app.RunAsync().ConfigureAwait(false);
return 0;
