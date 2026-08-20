using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.Identity;
using Ingot.Platform.Inspections.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotInspectionInfrastructure(builder.Configuration);
builder.Services.AddIngotPlatformWorkers();
builder.Services.AddIngotLocalIdentityMaintenance();

var host = builder.Build();
await host.RunAsync();
