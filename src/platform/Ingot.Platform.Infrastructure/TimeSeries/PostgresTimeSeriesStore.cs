using System.Text.Json;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.TimeSeries;

/// <summary>
/// TimescaleDB implementation of the canonical signal model. Production events remain
/// the immutable audit source; process samples are projected into this typed model in
/// the same database transaction as their source event.
/// </summary>
public sealed class PostgresTimeSeriesStore : ITimeSeriesStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] StaticTagKeys =
    [
        "tenant_id",
        "factory_id",
        "plant_id",
        "line_id",
        "workcell_id",
        "machine_id",
        "equipment_id",
        "product_series"
    ];

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresTimeSeriesStore> _logger;
    private readonly PlatformEventOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresTimeSeriesStore(
        IConfiguration configuration,
        ILogger<PostgresTimeSeriesStore> logger,
        IOptions<PlatformEventOptions> options)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
        _options = options.Value;
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
                CREATE EXTENSION IF NOT EXISTS timescaledb;

                CREATE TABLE IF NOT EXISTS signal_definitions (
                  data_model_id      TEXT NOT NULL,
                  data_model_version INTEGER NOT NULL,
                  signal_code        TEXT NOT NULL,
                  source_field       TEXT NOT NULL,
                  data_type          TEXT NOT NULL,
                  unit               TEXT,
                  category           TEXT NOT NULL,
                  definition_hash    TEXT NOT NULL,
                  first_seen_at      TIMESTAMPTZ NOT NULL,
                  last_seen_at       TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (data_model_id, data_model_version, signal_code)
                );

                CREATE TABLE IF NOT EXISTS collection_points (
                  collection_point_id TEXT PRIMARY KEY,
                  edge_id              TEXT NOT NULL,
                  subject_type         TEXT NOT NULL,
                  subject_id           TEXT NOT NULL,
                  signal_code          TEXT NOT NULL,
                  static_tags          JSONB NOT NULL DEFAULT '{}'::jsonb,
                  first_seen_at        TIMESTAMPTZ NOT NULL,
                  last_seen_at         TIMESTAMPTZ NOT NULL
                );

                CREATE TABLE IF NOT EXISTS time_series_samples (
                  occurred_at          TIMESTAMPTZ NOT NULL,
                  collection_point_id  TEXT NOT NULL,
                  signal_code          TEXT NOT NULL,
                  data_type            TEXT NOT NULL,
                  unit                 TEXT,
                  category             TEXT NOT NULL,
                  numeric_value        DOUBLE PRECISION,
                  integer_value        BIGINT,
                  boolean_value        BOOLEAN,
                  text_value           TEXT,
                  quality_code         TEXT NOT NULL,
                  event_id             TEXT NOT NULL,
                  ingest_id            BIGINT NOT NULL,
                  recorded_at          TIMESTAMPTZ NOT NULL,
                  edge_id              TEXT NOT NULL,
                  source               TEXT NOT NULL,
                  subject_type         TEXT NOT NULL,
                  subject_id           TEXT NOT NULL,
                  correlation_id       TEXT,
                  phase_code           TEXT,
                  data_model_id        TEXT NOT NULL,
                  data_model_version   INTEGER NOT NULL,
                  run_context          JSONB NOT NULL DEFAULT '{}'::jsonb,
                  CONSTRAINT ck_time_series_samples_one_value CHECK (
                    num_nonnulls(numeric_value, integer_value, boolean_value, text_value) = 1
                  ),
                  CONSTRAINT ck_time_series_samples_quality CHECK (
                    quality_code IN ('good', 'uncertain', 'bad')
                  )
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_time_series_samples_source
                  ON time_series_samples (event_id, signal_code, occurred_at);
                CREATE INDEX IF NOT EXISTS ix_time_series_samples_point_time
                  ON time_series_samples (collection_point_id, occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_time_series_samples_signal_time
                  ON time_series_samples (signal_code, occurred_at DESC);
                CREATE INDEX IF NOT EXISTS ix_time_series_samples_correlation
                  ON time_series_samples (correlation_id, signal_code, occurred_at);
                CREATE INDEX IF NOT EXISTS ix_time_series_samples_context
                  ON time_series_samples USING GIN (run_context);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            var chunkInterval = NormalizeInterval(_options.ChunkTimeInterval);
            await using (var hypertable = _dataSource.CreateCommand(
                             $"SELECT create_hypertable('time_series_samples', 'occurred_at', "
                             + $"chunk_time_interval => INTERVAL '{chunkInterval}', "
                             + "if_not_exists => TRUE, migrate_data => TRUE);"))
            {
                await hypertable.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }
            if (_options.CompressAfterDays > 0)
            {
                await using var compress = _dataSource.CreateCommand(
                    "ALTER TABLE time_series_samples SET ("
                    + "timescaledb.compress, "
                    + "timescaledb.compress_segmentby = 'collection_point_id', "
                    + "timescaledb.compress_orderby = 'occurred_at DESC');"
                    + $"SELECT add_compression_policy('time_series_samples', "
                    + $"INTERVAL '{_options.CompressAfterDays} days', if_not_exists => TRUE);");
                await compress.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }
            if (_options.RetentionDays > 0)
            {
                await using var retention = _dataSource.CreateCommand(
                    $"SELECT add_retention_policy('time_series_samples', "
                    + $"INTERVAL '{_options.RetentionDays} days', if_not_exists => TRUE);");
                await retention.ExecuteScalarAsync(ct).ConfigureAwait(false);
            }
            _initialized = true;
            _logger.LogInformation("标准测点时序存储结构已就绪");
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    internal async Task ProjectEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        long ingestId,
        ProductionEvent evt,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var samples = TimeSeriesSampleProjector.Project(edgeId, ingestId, evt, analysis);
        if (samples.Count == 0 || analysis is null)
            return;

        foreach (var definition in analysis.DataModel.Acquisition.DataItems
                     .Where(definition => samples.Any(sample =>
                         string.Equals(sample.SignalCode, definition.Code, StringComparison.Ordinal))))
        {
            await UpsertSignalDefinitionAsync(
                connection,
                transaction,
                analysis,
                definition.Code,
                definition.SourceField,
                definition.DataType,
                definition.Unit,
                definition.Category,
                evt.OccurredAt,
                ct).ConfigureAwait(false);
        }

        foreach (var sample in samples)
        {
            await UpsertCollectionPointAsync(connection, transaction, sample, ct).ConfigureAwait(false);
            await InsertSampleAsync(connection, transaction, sample, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<SignalSample>> QueryAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);
        var limit = Math.Clamp(query.Limit, 1, 100_000);
        var filters = new List<string>();
        await using var command = _dataSource.CreateCommand();
        if (!string.IsNullOrWhiteSpace(query.CollectionPointId))
        {
            filters.Add("collection_point_id = @collection_point_id");
            command.Parameters.AddWithValue("collection_point_id", query.CollectionPointId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SignalCode))
        {
            filters.Add("signal_code = @signal_code");
            command.Parameters.AddWithValue("signal_code", query.SignalCode.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            filters.Add("correlation_id = @correlation_id");
            command.Parameters.AddWithValue("correlation_id", query.CorrelationId.Trim());
        }
        if (query.From.HasValue)
        {
            filters.Add("occurred_at >= @from");
            command.Parameters.AddWithValue("from", query.From.Value.UtcDateTime);
        }
        if (query.To.HasValue)
        {
            filters.Add("occurred_at < @to");
            command.Parameters.AddWithValue("to", query.To.Value.UtcDateTime);
        }
        command.Parameters.AddWithValue("limit", limit);
        var where = filters.Count == 0 ? "" : $"WHERE {string.Join(" AND ", filters)}";
        command.CommandText =
            $"""
             SELECT collection_point_id, signal_code, data_type, unit, category,
                    occurred_at, recorded_at, event_id, ingest_id, edge_id, source,
                    subject_type, subject_id, correlation_id, phase_code,
                    data_model_id, data_model_version, quality_code,
                    numeric_value, integer_value, boolean_value, text_value, run_context
             FROM time_series_samples
             {where}
             ORDER BY occurred_at, ingest_id, signal_code
             LIMIT @limit;
             """;
        var result = new List<SignalSample>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SignalSample
            {
                CollectionPointId = reader.GetString(0),
                SignalCode = reader.GetString(1),
                DataType = reader.GetString(2),
                Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                Category = reader.GetString(4),
                OccurredAt = new DateTimeOffset(reader.GetDateTime(5)),
                RecordedAt = new DateTimeOffset(reader.GetDateTime(6)),
                EventId = reader.GetString(7),
                IngestId = reader.GetInt64(8),
                EdgeId = reader.GetString(9),
                Source = reader.GetString(10),
                SubjectType = reader.GetString(11),
                SubjectId = reader.GetString(12),
                CorrelationId = reader.IsDBNull(13) ? null : reader.GetString(13),
                PhaseCode = reader.IsDBNull(14) ? null : reader.GetString(14),
                DataModelId = reader.GetString(15),
                DataModelVersion = reader.GetInt32(16),
                QualityCode = reader.GetString(17),
                NumericValue = reader.IsDBNull(18) ? null : reader.GetDouble(18),
                IntegerValue = reader.IsDBNull(19) ? null : reader.GetInt64(19),
                BooleanValue = reader.IsDBNull(20) ? null : reader.GetBoolean(20),
                TextValue = reader.IsDBNull(21) ? null : reader.GetString(21),
                RunContext = JsonSerializer.Deserialize<Dictionary<string, string>>(
                                 reader.GetString(22),
                                 JsonOptions)
                             ?? new Dictionary<string, string>()
            });
        }
        return result;
    }

    private static async Task UpsertSignalDefinitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResolvedProcessAnalysis analysis,
        string code,
        string sourceField,
        string dataType,
        string? unit,
        string category,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var definitionHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"{analysis.DataModel.ModelId}|{analysis.DataModel.Version}|{code}|{sourceField}|{dataType}|{unit}|{category}")))
            .ToLowerInvariant();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO signal_definitions (
              data_model_id, data_model_version, signal_code, source_field,
              data_type, unit, category, definition_hash, first_seen_at, last_seen_at)
            VALUES (
              @model_id, @model_version, @signal_code, @source_field,
              @data_type, @unit, @category, @definition_hash, @seen_at, @seen_at)
            ON CONFLICT (data_model_id, data_model_version, signal_code)
            DO UPDATE SET last_seen_at = GREATEST(signal_definitions.last_seen_at, EXCLUDED.last_seen_at);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("model_id", analysis.DataModel.ModelId);
        command.Parameters.AddWithValue("model_version", analysis.DataModel.Version);
        command.Parameters.AddWithValue("signal_code", code);
        command.Parameters.AddWithValue("source_field", sourceField);
        command.Parameters.AddWithValue("data_type", dataType);
        command.Parameters.AddWithValue("unit", (object?)unit ?? DBNull.Value);
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("definition_hash", definitionHash);
        command.Parameters.AddWithValue("seen_at", occurredAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertCollectionPointAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SignalSample sample,
        CancellationToken ct)
    {
        var staticTags = sample.RunContext
            .Where(pair => StaticTagKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO collection_points (
              collection_point_id, edge_id, subject_type, subject_id, signal_code,
              static_tags, first_seen_at, last_seen_at)
            VALUES (
              @point_id, @edge_id, @subject_type, @subject_id, @signal_code,
              @static_tags, @seen_at, @seen_at)
            ON CONFLICT (collection_point_id)
            DO UPDATE SET
              static_tags = collection_points.static_tags || EXCLUDED.static_tags,
              last_seen_at = GREATEST(collection_points.last_seen_at, EXCLUDED.last_seen_at);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("point_id", sample.CollectionPointId);
        command.Parameters.AddWithValue("edge_id", sample.EdgeId);
        command.Parameters.AddWithValue("subject_type", sample.SubjectType);
        command.Parameters.AddWithValue("subject_id", sample.SubjectId);
        command.Parameters.AddWithValue("signal_code", sample.SignalCode);
        command.Parameters.AddWithValue(
            "static_tags",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(staticTags, JsonOptions));
        command.Parameters.AddWithValue("seen_at", sample.OccurredAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertSampleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SignalSample sample,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO time_series_samples (
              occurred_at, collection_point_id, signal_code, data_type, unit, category,
              numeric_value, integer_value, boolean_value, text_value, quality_code,
              event_id, ingest_id, recorded_at, edge_id, source, subject_type, subject_id,
              correlation_id, phase_code, data_model_id, data_model_version, run_context)
            VALUES (
              @occurred_at, @point_id, @signal_code, @data_type, @unit, @category,
              @numeric_value, @integer_value, @boolean_value, @text_value, @quality_code,
              @event_id, @ingest_id, @recorded_at, @edge_id, @source, @subject_type, @subject_id,
              @correlation_id, @phase_code, @model_id, @model_version, @run_context)
            ON CONFLICT (event_id, signal_code, occurred_at) DO NOTHING;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("occurred_at", sample.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("point_id", sample.CollectionPointId);
        command.Parameters.AddWithValue("signal_code", sample.SignalCode);
        command.Parameters.AddWithValue("data_type", sample.DataType);
        command.Parameters.AddWithValue("unit", (object?)sample.Unit ?? DBNull.Value);
        command.Parameters.AddWithValue("category", sample.Category);
        command.Parameters.AddWithValue("numeric_value", (object?)sample.NumericValue ?? DBNull.Value);
        command.Parameters.AddWithValue("integer_value", (object?)sample.IntegerValue ?? DBNull.Value);
        command.Parameters.AddWithValue("boolean_value", (object?)sample.BooleanValue ?? DBNull.Value);
        command.Parameters.AddWithValue("text_value", (object?)sample.TextValue ?? DBNull.Value);
        command.Parameters.AddWithValue("quality_code", sample.QualityCode);
        command.Parameters.AddWithValue("event_id", sample.EventId);
        command.Parameters.AddWithValue("ingest_id", sample.IngestId);
        command.Parameters.AddWithValue("recorded_at", sample.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("edge_id", sample.EdgeId);
        command.Parameters.AddWithValue("source", sample.Source);
        command.Parameters.AddWithValue("subject_type", sample.SubjectType);
        command.Parameters.AddWithValue("subject_id", sample.SubjectId);
        command.Parameters.AddWithValue("correlation_id", (object?)sample.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("phase_code", (object?)sample.PhaseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("model_id", sample.DataModelId);
        command.Parameters.AddWithValue("model_version", sample.DataModelVersion);
        command.Parameters.AddWithValue(
            "run_context",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(sample.RunContext, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string NormalizeInterval(string configured)
        => System.Text.RegularExpressions.Regex.IsMatch(
            configured.Trim(),
            @"^\d+\s+(second|minute|hour|day|week|month|year)s?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            ? configured.Trim()
            : "30 days";

    public ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        return _dataSource.DisposeAsync();
    }
}
