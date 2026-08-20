// 作为 Ingot.Platform.Worker 的组合根，集中完成配置校验、依赖注册和宿主启动。

using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.Identity;
using Ingot.Platform.Infrastructure.Inspections;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotInspectionInfrastructure(builder.Configuration);
builder.Services.AddIngotPlatformWorkers();
builder.Services.AddIngotLocalIdentityMaintenance();

var host = builder.Build();
await host.RunAsync();
