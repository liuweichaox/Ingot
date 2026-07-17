// Central API（中心侧）：提供中心 API（边缘注册/心跳、诊断代理、查询与管理）。

using Ingot.Central.Api.HealthChecks;
using Ingot.Central.Api.Events;
using Ingot.Central.Api.Webhooks;
using Serilog;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// 从配置读取 URL，支持环境变量和配置文件
var urls = builder.Configuration["Urls"] ?? builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:8000";
builder.WebHost.UseUrls(urls);

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("webhook", (services, client) =>
{
    var webhookOptions = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebhookOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(webhookOptions.RequestTimeoutSeconds, 1, 300));
});
builder.Services.AddControllers();
builder.Services.AddSingleton<Ingot.Central.Api.Services.EdgeRegistry>();
builder.Services.Configure<CentralEventOptions>(builder.Configuration.GetSection("EventIngest"));
builder.Services.AddSingleton<CentralEventMetrics>();
builder.Services.AddSingleton<EdgeTokenValidator>();
builder.Services.AddSingleton<ICentralEventStore, PostgresEventStore>();
builder.Services.AddHostedService<EventStoreInitializerHostedService>();
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhook"));
builder.Services.AddSingleton<IWebhookSubscriptionStore, PostgresWebhookSubscriptionStore>();
builder.Services.AddSingleton<WebhookDispatcher>();
builder.Services.AddSingleton<WebhookMetrics>();
builder.Services.AddHostedService<WebhookStoreInitializerHostedService>();
builder.Services.AddHostedService<WebhookDeliveryHostedService>();

builder.Services
    .AddHealthChecks()
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
            // 默认允许本地 Vue dev server
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
        subscriptions = "/api/v1/subscriptions"
    }
}));

// 解析并显示所有监听地址
var addresses = urls.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var baseAddress = addresses.FirstOrDefault()?.Trim() ?? "http://localhost:8000";

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
Log.Logger.Information("    > Subscriptions: {0}/api/v1/subscriptions", baseAddress);
Log.Logger.Information("==================================================================");

app.Run();
