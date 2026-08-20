using Ingot.Platform.Infrastructure.Migrations;
using Ingot.Platform.Infrastructure.Identity;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<MigrationRunner>();
builder.Services.AddIngotLocalIdentityBootstrap(builder.Configuration);

using var host = builder.Build();
await host.Services.GetRequiredService<MigrationRunner>().RunAsync();
await host.Services.GetRequiredService<LocalAdminBootstrapper>().RunAsync();
