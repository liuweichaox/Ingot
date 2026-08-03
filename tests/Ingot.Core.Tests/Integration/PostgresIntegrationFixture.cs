using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ingot.Platform.Infrastructure.Migrations;
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
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _schemaReady;

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

    public async Task EnsureSchemaAsync()
    {
        if (_schemaReady)
            return;
        await _migrationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_schemaReady)
                return;
            await new MigrationRunner(Configuration, NullLogger<MigrationRunner>.Instance)
                .RunAsync().ConfigureAwait(false);
            _schemaReady = true;
        }
        finally
        {
            _migrationLock.Release();
        }
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
