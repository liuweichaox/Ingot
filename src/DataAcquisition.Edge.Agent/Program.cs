// Edge Agent（车间侧）：负责 PLC 采集、直写 TSDB、上报中心，以及本地诊断 API（不包含 UI）。

using DataAcquisition.Application.Abstractions;
using DataAcquisition.Domain.Models;
using DataAcquisition.Infrastructure.Clients;
using DataAcquisition.Infrastructure.DataAcquisitions;
using DataAcquisition.Infrastructure.DataStorages;
using DataAcquisition.Infrastructure.DeviceConfigs;
using DataAcquisition.Infrastructure.Logs;
using DataAcquisition.Infrastructure.Metrics;
using DataAcquisition.Infrastructure.Queues;
using DataAcquisition.Edge.Agent.BackgroundServices;
using DataAcquisition.Edge.Agent.Services;
using Prometheus;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var validateConfigsOnly = HasOption(args, "--validate-configs");
var configDirectoryOverride = GetOptionValue(args, "--config-dir");

var builder = WebApplication.CreateBuilder(args);

// 支持通过配置/环境变量指定监听地址
var urls = builder.Configuration["Urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:8001";
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();

// 配置 AcquisitionOptions
builder.Services.Configure<AcquisitionOptions>(builder.Configuration.GetSection("Acquisition"));
builder.Services.Configure<LogOptions>(builder.Configuration.GetSection("Logging"));

// 配置 Edge 上报（注册/心跳）
builder.Services.Configure<EdgeReportingOptions>(builder.Configuration.GetSection("Edge"));
builder.Services.AddSingleton<EdgeIdentityService>();

builder.Services.AddSingleton<IMetricsCollector, MetricsCollector>();
builder.Services.AddSingleton<MetricsBridge>();
builder.Services.AddSingleton<IDeviceConfigService, DeviceConfigService>();
builder.Services.AddSingleton<DeviceConfigValidationRunner>();
builder.Services.AddSingleton<IPlcDriverProvider, HslStandardPlcDriverProvider>();
builder.Services.AddSingleton<IPlcClientFactory, PlcClientFactory>();
builder.Services.AddSingleton<IPlcClientLifecycleService, PlcClientLifecycleService>();
builder.Services.AddSingleton<IAcquisitionStateManager, AcquisitionStateManager>();
builder.Services.AddSingleton<IHeartbeatMonitor, HeartbeatMonitor>();
builder.Services.AddSingleton<IChannelCollector, ChannelCollector>();

// 存储：默认直写 InfluxDB，存储实现可替换
builder.Services.AddSingleton<IDataStorageService, InfluxDbDataStorageService>();
builder.Services.AddSingleton<IQueueService, QueueService>();
builder.Services.AddSingleton<IDataAcquisitionService, DataAcquisitionService>();

// 日志查看服务（使用 SQLite）
builder.Services.AddSingleton<ILogViewService, SqliteLogViewService>();

builder.Services.AddHostedService<DataAcquisitionHostedService>();
builder.Services.AddHostedService<EdgeCentralReporterHostedService>();

builder.Services.AddControllers();

// Health checks（官方风格）：统一用 /health
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("ok"));

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

if (validateConfigsOnly)
{
    var validationRunner = app.Services.GetRequiredService<DeviceConfigValidationRunner>();
    var acquisitionOptions = app.Services.GetRequiredService<IOptions<AcquisitionOptions>>().Value;
    var configDirectory = string.IsNullOrWhiteSpace(configDirectoryOverride)
        ? DeviceConfigPathResolver.Resolve(acquisitionOptions.DeviceConfigService.ConfigDirectory)
        : Path.GetFullPath(configDirectoryOverride);

    var summary = await validationRunner.ValidateDirectoryAsync(configDirectory).ConfigureAwait(false);
    Log.Logger.Information("配置校验目录: {ConfigDirectory}", summary.Directory);

    foreach (var file in summary.Files)
    {
        if (file.IsValid)
        {
            Log.Logger.Information("  [OK] {FilePath} ({PlcCode})", file.FilePath, file.PlcCode ?? "unknown");
            continue;
        }

        Log.Logger.Error("  [ERROR] {FilePath}", file.FilePath);
        foreach (var error in file.Errors)
            Log.Logger.Error("    - {Error}", error);
    }

    return summary.IsValid ? 0 : 1;
}

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
    service = "DataAcquisition.Edge.Agent",
    endpoints = new
    {
        health = "/health",
        metrics = "/metrics",
        logs = "/api/logs",
        logLevels = "/api/logs/levels",
        plcConnections = "/api/DataAcquisition/plc-connections",
        writeRegister = "/api/DataAcquisition/WriteRegister"
    }
}));

// 解析并显示所有监听地址
var addresses = urls.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var baseAddress = addresses.FirstOrDefault()?.Trim() ?? "http://localhost:8001";

Log.Logger.Information("==================================================================");
Log.Logger.Information("              Edge Agent Service Started");
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
Log.Logger.Information("==================================================================");

await app.RunAsync().ConfigureAwait(false);
return 0;

static bool HasOption(string[] args, string option)
{
    return args.Any(arg => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase));
}

static string? GetOptionValue(string[] args, string option)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}
