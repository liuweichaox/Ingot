using Ingot.Platform.Infrastructure.Migrations;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    [LinuxDockerFact]
    public async Task TimeSeriesSamples_ShouldUseSingleSourceSchema()
    {
        var runner = new MigrationRunner(postgres.Configuration, NullLogger<MigrationRunner>.Instance);
        await runner.RunAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
              to_regclass('public.time_series_samples') IS NULL
              AND to_regclass('public.process_sample_frames') IS NOT NULL
              AND to_regclass('public.process_sample_values') IS NOT NULL
              AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'process_sample_frames'
                  AND column_name = 'ingested_at')
              AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'collection_points'
                  AND column_name = 'point_key' AND data_type = 'bigint')
              AND EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'process_sample_values'
                  AND column_name = 'quality_code' AND data_type = 'smallint')
              AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'process_sample_values'
                  AND column_name IN (
                    'event_id', 'execution_id', 'edge_id', 'source', 'subject_type',
                    'subject_id', 'data_model_id', 'data_model_version', 'signal_code',
                    'collection_point_id'))
              AND to_regclass('public.ix_time_series_samples_context') IS NULL
              AND to_regclass('public.production_event_stream') IS NULL
              AND to_regclass('public.projected_process_sample_events') IS NULL;
            """,
            connection);
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [LinuxDockerFact]
    public async Task TimeSeriesRetention_ShouldPruneFramesAndValuesTogether()
    {
        await postgres.EnsureSchemaAsync();
        await using var store = new PostgresTimeSeriesStore(
            postgres.Configuration,
            NullLogger<PostgresTimeSeriesStore>.Instance,
            Options.Create(new PlatformEventOptions()));
        await store.InitializeAsync();
        var frameId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var occurredAt = DateTimeOffset.UtcNow.AddDays(-400);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO process_sample_frames (
                           occurred_at, frame_id, event_id, recorded_at, ingested_at,
                           edge_id, source, subject_type, subject_id, data_model_id, data_model_version)
                         VALUES (
                           @at, @frame_id, @event_id, @at, @at,
                           'EDGE-RETENTION', 'test', 'equipment', 'PRESS-RETENTION', 'retention-model', 1);
                         INSERT INTO process_sample_values (
                           occurred_at, frame_id, point_key, quality_code, numeric_value)
                         VALUES (@at, @frame_id, 1, 0, 1.0);
                         """,
                         connection))
        {
            insert.Parameters.AddWithValue("at", occurredAt.UtcDateTime);
            insert.Parameters.AddWithValue("frame_id", frameId);
            insert.Parameters.AddWithValue("event_id", $"retention-{frameId}");
            await insert.ExecuteNonQueryAsync();
        }

        await TimeSeriesRetentionHostedService.PruneAsync(postgres.ConnectionString, 90);

        await using var count = new NpgsqlCommand(
            """
            SELECT
              (SELECT count(*) FROM process_sample_frames WHERE frame_id = @frame_id),
              (SELECT count(*) FROM process_sample_values WHERE frame_id = @frame_id);
            """,
            connection);
        count.Parameters.AddWithValue("frame_id", frameId);
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal(0, reader.GetInt64(1));
    }
}
