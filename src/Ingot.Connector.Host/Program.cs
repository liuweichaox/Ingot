// Connector Host：接收用户接入程序产生的标准事件并上报中心。

using Ingot.Application.Abstractions;
using Ingot.Application.Options;
using Ingot.Infrastructure.Events;
using Ingot.Infrastructure.Logs;
using Ingot.Infrastructure.Metrics;
using Ingot.Infrastructure.Reporting;
using Ingot.Infrastructure.State;
using Ingot.Connector.Host.BackgroundServices;
using Ingot.Connector.Host.HealthChecks;
using Ingot.Connector.Host.Services;
using Ingot.Connector.Host.Configuration;
using Prometheus;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment())
    ProductionConfigurationValidator.Validate(builder.Configuration);

var urls = builder.Configuration["Urls"]
    ?? throw new InvalidOperationException("Urls is required.");
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();

builder.Services.Configure<Ingot.Domain.Events.EventOptions>(builder.Configuration.GetSection("Events"));
builder.Services.Configure<LogOptions>(builder.Configuration.GetSection("Logging"));

// 配置 Edge 上报（注册/心跳）
builder.Services.Configure<EdgeReportingOptions>(builder.Configuration.GetSection("Edge"));
builder.Services.AddSingleton<EdgeIdentityService>();
builder.Services.AddSingleton<IEdgeIdentityProvider>(services => services.GetRequiredService<EdgeIdentityService>());
builder.Services.AddSingleton<ICentralReportingClient, CentralReportingClient>();

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddSingleton<MetricsBridge>();
builder.Services.AddSingleton<IEdgeContextStore, ContextStore>();
builder.Services.AddSingleton<IEventLog, SqliteEventLog>();
builder.Services.AddSingleton<IEventPersistenceHealth, EventPersistenceHealth>();
builder.Services.AddSingleton<IEventSink, EventSink>();
builder.Services.AddSingleton<IEventShipper, HttpEventShipper>();

// 日志查看服务（使用 SQLite）
builder.Services.AddSingleton<ILogViewService, SqliteLogViewService>();

builder.Services.AddHostedService<EdgeCentralReporterHostedService>();
builder.Services.AddHostedService<EventShipperHostedService>();

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
    service = "Ingot.Connector.Host",
    endpoints = new
    {
        health = "/health",
        metrics = "/metrics",
        logs = "/api/logs",
        logLevels = "/api/logs/levels",
        connectorEvents = "/api/v1/connector-events",
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
