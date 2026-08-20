using Ingot.Platform.Infrastructure;
using Ingot.Platform.Inspections.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotInspectionInfrastructure(builder.Configuration);
builder.Services.AddIngotPlatformWorkers();

var host = builder.Build();
await host.RunAsync();
