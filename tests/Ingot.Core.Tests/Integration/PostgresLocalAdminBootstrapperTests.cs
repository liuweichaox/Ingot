using Ingot.Platform.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresLocalAdminBootstrapperTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ConcurrentBootstrap_ShouldCreateExactlyOneInitialUser()
    {
        await postgres.EnsureSchemaAsync();
        await ResetUsersAsync();
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Mode"] = "Local",
                    ["Authentication:Local:SeedAdminUsername"] = "bootstrap-admin",
                    ["Authentication:Local:SeedAdminPassword"] = "bootstrap-password"
                })
                .Build();
            var options = Options.Create(new LocalAuthOptions
            {
                SeedAdminUsername = "bootstrap-admin",
                SeedAdminPassword = "bootstrap-password"
            });
            var environment = new ProductionHostEnvironment();
            var first = new LocalAdminBootstrapper(
                postgres.DataSource,
                new LocalPasswordHasher(),
                options,
                configuration,
                environment,
                NullLogger<LocalAdminBootstrapper>.Instance);
            var second = new LocalAdminBootstrapper(
                postgres.DataSource,
                new LocalPasswordHasher(),
                options,
                configuration,
                environment,
                NullLogger<LocalAdminBootstrapper>.Instance);

            await Task.WhenAll(first.RunAsync(), second.RunAsync());

            await using var command = postgres.DataSource.CreateCommand(
                "SELECT count(*), min(username) FROM users;");
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt64(0));
            Assert.Equal("bootstrap-admin", reader.GetString(1));
        }
        finally
        {
            await ResetUsersAsync();
        }
    }

    private async Task ResetUsersAsync()
    {
        await using var command = postgres.DataSource.CreateCommand(
            "TRUNCATE TABLE user_sessions, users CASCADE;");
        await command.ExecuteNonQueryAsync();
    }

    private sealed class ProductionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
