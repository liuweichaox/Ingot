using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresIntegrationFixture>
{
    public const string Name = "postgres-integration";
}

public sealed class PostgresIntegrationFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => (_container ??
        throw new InvalidOperationException("The PostgreSQL integration fixture is not running."))
        .GetConnectionString();

    public IConfiguration Configuration => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = ConnectionString,
            ["Database:SchemaManagement"] = "Migrations"
        })
        .Build();

    public async Task InitializeAsync()
    {
        if (OperatingSystem.IsWindows())
            return;
        _container = new PostgreSqlBuilder("timescale/timescaledb:2.28.3-pg17")
            .WithDatabase("ingot_test")
            .WithUsername("ingot")
            .WithPassword("ingot-test-password")
            .Build();
        await _container.StartAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class LinuxDockerFactAttribute : FactAttribute
{
    public LinuxDockerFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Real PostgreSQL tests run in the WSL/Linux Docker verification gate.";
    }
}
