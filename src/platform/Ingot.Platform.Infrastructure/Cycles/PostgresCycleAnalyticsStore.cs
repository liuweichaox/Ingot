using Npgsql;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class PostgresCycleAnalyticsStore : ICycleAnalyticsStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresCycleAnalyticsStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;
        await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS cycle_phases (
                  correlation_id TEXT NOT NULL,
                  phase_code TEXT NOT NULL,
                  ordinal INTEGER NOT NULL,
                  started_at TIMESTAMPTZ NOT NULL,
                  completed_at TIMESTAMPTZ,
                  provenance TEXT NOT NULL,
                  source_event_id_start TEXT,
                  source_event_id_end TEXT,
                  PRIMARY KEY (correlation_id, phase_code, ordinal)
                );
                CREATE INDEX IF NOT EXISTS idx_cycle_phases_phase_time
                  ON cycle_phases(phase_code, started_at);

                CREATE TABLE IF NOT EXISTS cycle_features (
                  correlation_id TEXT NOT NULL,
                  phase_code TEXT NOT NULL,
                  feature_code TEXT NOT NULL,
                  numeric_value DOUBLE PRECISION,
                  unit TEXT,
                  sample_count INTEGER NOT NULL DEFAULT 0,
                  provenance TEXT NOT NULL,
                  computed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                  PRIMARY KEY (correlation_id, phase_code, feature_code)
                );
                CREATE INDEX IF NOT EXISTS idx_cycle_features_feature
                  ON cycle_features(feature_code, phase_code, numeric_value);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task RebuildCycleAsync(string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId 不能为空。", nameof(correlationId));
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var delete = new NpgsqlCommand(
                         """
                         DELETE FROM cycle_features WHERE correlation_id = @correlation_id;
                         DELETE FROM cycle_phases WHERE correlation_id = @correlation_id;
                         """,
                         connection,
                         transaction))
        {
            delete.Parameters.AddWithValue("correlation_id", correlationId.Trim());
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var phases = new NpgsqlCommand(
                         """
                         WITH step_events AS (
                           SELECT event_id,
                                  occurred_at,
                                  correlation_id,
                                  context->>'recipe_id' AS recipe_id,
                                  context->>'recipe_version' AS recipe_version,
                                  context->>'recipe_template' AS recipe_template,
                                  context->>'recipe_step' AS recipe_step,
                                  lead(occurred_at) OVER (ORDER BY occurred_at, ingest_id) AS next_at,
                                  lead(event_id) OVER (ORDER BY occurred_at, ingest_id) AS next_event_id
                           FROM production_events
                           WHERE correlation_id = @correlation_id
                             AND context ? 'recipe_step'
                         ),
                         mapped AS (
                           SELECT step_events.*,
                                  coalesce(phase_mappings.phase_code, 'unknown') AS phase_code,
                                  coalesce((phase_mappings.payload->>'provenance'), 'inferred') AS provenance
                           FROM step_events
                           LEFT JOIN phase_mappings
                             ON phase_mappings.recipe_id = step_events.recipe_id
                            AND phase_mappings.recipe_step = step_events.recipe_step
                            AND (phase_mappings.recipe_version IS NULL OR phase_mappings.recipe_version = step_events.recipe_version)
                            AND (phase_mappings.recipe_template IS NULL OR phase_mappings.recipe_template = step_events.recipe_template)
                         ),
                         markers AS (
                           SELECT *,
                                  CASE WHEN lag(phase_code) OVER (ORDER BY occurred_at, event_id) = phase_code THEN 0 ELSE 1 END AS new_island
                           FROM mapped
                         ),
                         islands AS (
                           SELECT *,
                                  sum(new_island) OVER (ORDER BY occurred_at, event_id) AS island_id
                           FROM markers
                         )
                         INSERT INTO cycle_phases(
                           correlation_id, phase_code, ordinal, started_at, completed_at, provenance,
                           source_event_id_start, source_event_id_end)
                         SELECT @correlation_id,
                                phase_code,
                                row_number() OVER (PARTITION BY phase_code ORDER BY min(occurred_at))::int,
                                min(occurred_at),
                                max(next_at),
                                CASE WHEN bool_or(provenance = 'inferred') THEN 'inferred' ELSE 'configured' END,
                                (array_agg(event_id ORDER BY occurred_at))[1],
                                (array_agg(next_event_id ORDER BY occurred_at DESC))[1]
                         FROM islands
                         GROUP BY phase_code, island_id
                         HAVING max(next_at) IS NOT NULL;
                         """,
                         connection,
                         transaction))
        {
            phases.Parameters.AddWithValue("correlation_id", correlationId.Trim());
            await phases.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}
