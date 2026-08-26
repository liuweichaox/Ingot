// 组合独立 Platform Worker 的后台任务、健康检查、指标和生命周期。
using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.Identity;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotInspectionInfrastructure(builder.Configuration);
builder.Services.AddIngotPlatformWorkers(builder.Configuration);
builder.Services.AddIngotLocalIdentityMaintenance();
builder.Services.AddHealthChecks()
    .AddCheck<PlatformWorkerPulseHealthCheck>("worker-heartbeat");

var app = builder.Build();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    AllowCachingResponses = false
});
app.MapGet("/metrics", (PlatformWorkerPulse pulse, IOptions<PlatformWorkerPulseOptions> options) => Results.Text(
    pulse.RenderPrometheus(options.Value.StaleAfter),
    "text/plain; version=0.0.4; charset=utf-8"));
await app.RunAsync();
