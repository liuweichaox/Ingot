using Ingot.Platform.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddIngotPlatformInfrastructure(builder.Configuration);
builder.Services.AddIngotPlatformWorkers();

var host = builder.Build();
await host.RunAsync();
