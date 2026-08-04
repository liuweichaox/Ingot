using Ingot.Platform.Infrastructure.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresMigrationTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ConcurrentRunners_ShouldApplyEveryMigrationExactlyOnce()
    {
        var first = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);
        var second = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);

        await Task.WhenAll(first.RunAsync(), second.RunAsync());
        await first.RunAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*), count(DISTINCT version) FROM schema_version;",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var expected = typeof(MigrationRunner).Assembly.GetManifestResourceNames().LongCount(name =>
            name.Contains(".Migrations.sql.", StringComparison.Ordinal) &&
            name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
        Assert.True(expected > 0);
        Assert.Equal(expected, reader.GetInt64(0));
        Assert.Equal(reader.GetInt64(0), reader.GetInt64(1));
    }

    [LinuxDockerFact]
    public async Task RemovedWebhookSchema_ShouldNotRemainAfterMigrations()
    {
        var runner = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);
        await runner.RunAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.webhook_subscriptions') IS NULL;",
            connection);
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }
}
