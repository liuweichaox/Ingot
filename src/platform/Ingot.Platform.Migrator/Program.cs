using Ingot.Platform.Infrastructure.Migrations;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<MigrationRunner>();

using var host = builder.Build();
await host.Services.GetRequiredService<MigrationRunner>().RunAsync();
