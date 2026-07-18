using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Central.Infrastructure.Events;

/// <summary>
///     PostgreSQL/JSONB 中心事实库。全局去重键与月度分区事实表分离，
///     同时保证 EventId、(EdgeId, Seq) 幂等和按月维护能力。
/// </summary>
public sealed class PostgresEventStore : ICentralEventStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresEventStore> _logger;
    private readonly CentralEventMetrics _metrics;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresEventStore(
        IConfiguration configuration,
        ILogger<PostgresEventStore> logger,
        CentralEventMetrics metrics)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
        _metrics = metrics;
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
                CREATE SEQUENCE IF NOT EXISTS production_events_ingest_id_seq;

                CREATE TABLE IF NOT EXISTS event_ingest_keys (
                  event_id    TEXT PRIMARY KEY,
                  edge_id     TEXT NOT NULL,
                  seq         BIGINT NOT NULL,
                  occurred_at TIMESTAMPTZ NOT NULL,
                  UNIQUE (edge_id, seq)
                );

                CREATE TABLE IF NOT EXISTS production_events (
                  ingest_id      BIGINT NOT NULL DEFAULT nextval('production_events_ingest_id_seq'),
                  event_id       TEXT NOT NULL,
                  edge_id        TEXT NOT NULL,
                  seq            BIGINT NOT NULL,
                  event_type     TEXT NOT NULL,
                  type_version   INTEGER NOT NULL,
                  occurred_at    TIMESTAMPTZ NOT NULL,
                  recorded_at    TIMESTAMPTZ NOT NULL,
                  ingested_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
                  source         TEXT NOT NULL,
                  subject_type   TEXT NOT NULL,
                  subject_id     TEXT NOT NULL,
                  correlation_id TEXT,
                  context        JSONB NOT NULL DEFAULT '{}'::jsonb,
                  data           JSONB NOT NULL DEFAULT '{}'::jsonb
                ) PARTITION BY RANGE (occurred_at);

                CREATE INDEX IF NOT EXISTS idx_production_events_ingest
                  ON production_events (ingest_id);
                CREATE INDEX IF NOT EXISTS idx_production_events_type_time
                  ON production_events (event_type, occurred_at DESC);
                CREATE INDEX IF NOT EXISTS idx_production_events_subject_time
                  ON production_events (subject_type, subject_id, occurred_at DESC);
                CREATE INDEX IF NOT EXISTS idx_production_events_correlation
                  ON production_events (correlation_id, occurred_at);
                CREATE INDEX IF NOT EXISTS idx_production_events_context
                  ON production_events USING GIN (context);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
            _logger.LogInformation("PostgreSQL 中心事件库结构已就绪");
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<EventBatchResponse> IngestAsync(
        EventBatchRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await InitializeAsync(ct).ConfigureAwait(false);

        var ordered = request.Events.OrderBy(static evt => evt.Seq).ToArray();
        if (ordered.Length == 0)
            return new EventBatchResponse();

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        foreach (var month in ordered.Select(static evt => MonthStart(evt.OccurredAt)).Distinct())
            await EnsurePartitionAsync(connection, transaction, month, ct).ConfigureAwait(false);

        var previousMax = await GetMaxEdgeSeqAsync(connection, transaction, request.EdgeId, ct)
            .ConfigureAwait(false);
        var gapDetected = HasSequenceGap(previousMax, ordered);
        var accepted = 0;
        var duplicates = 0;

        foreach (var evt in ordered)
        {
            if (await TryReserveEventAsync(connection, transaction, request.EdgeId, evt, ct)
                    .ConfigureAwait(false))
            {
                await InsertEventAsync(connection, transaction, request.EdgeId, evt, ct)
                    .ConfigureAwait(false);
                accepted++;
            }
            else
            {
                await VerifyDuplicateAsync(connection, transaction, request.EdgeId, evt, ct)
                    .ConfigureAwait(false);
                duplicates++;
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        var response = new EventBatchResponse
        {
            Accepted = accepted,
            Duplicates = duplicates,
            AckSeq = ordered[^1].Seq,
            GapDetected = gapDetected
        };
        try
        {
            _metrics.Record(request.EdgeId, accepted, duplicates, gapDetected);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "事件批次已经提交，但记录中心摄入指标失败：EdgeId={EdgeId}",
                request.EdgeId);
        }

        return response;
    }

    public async Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
        CentralEventQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand();
        var predicates = new List<string>();
        AddEquality(command, predicates, "edge_id", "edge_id", query.EdgeId);
        AddEquality(command, predicates, "event_type", "event_type", query.EventType);
        AddEquality(command, predicates, "subject_type", "subject_type", query.SubjectType);
        AddEquality(command, predicates, "subject_id", "subject_id", query.SubjectId);
        AddEquality(command, predicates, "correlation_id", "correlation_id", query.CorrelationId);

        if (query.From.HasValue)
        {
            predicates.Add("occurred_at >= @from");
            command.Parameters.AddWithValue("from", query.From.Value.UtcDateTime);
        }
        if (query.To.HasValue)
        {
            predicates.Add("occurred_at <= @to");
            command.Parameters.AddWithValue("to", query.To.Value.UtcDateTime);
        }
        if (query.AfterIngestId.HasValue)
        {
            predicates.Add("ingest_id > @after_ingest_id");
            command.Parameters.AddWithValue("after_ingest_id", query.AfterIngestId.Value);
        }

        var contextIndex = 0;
        foreach (var pair in query.Context)
        {
            if (!EventQueryContractValidator.IsValidContextKey(pair.Key))
                throw new ArgumentException($"非法上下文键: {pair.Key}", nameof(query));
            var keyName = $"ctx_key_{contextIndex}";
            var valueName = $"ctx_value_{contextIndex}";
            predicates.Add($"context ->> @{keyName} = @{valueName}");
            command.Parameters.AddWithValue(keyName, pair.Key);
            command.Parameters.AddWithValue(valueName, pair.Value);
            contextIndex++;
        }

        var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
        var order = query.AfterIngestId.HasValue ? "ASC" : "DESC";
        command.CommandText = $"""
                              SELECT ingest_id, edge_id, ingested_at, event_id, event_type, type_version,
                                     occurred_at, recorded_at, source, subject_type, subject_id,
                                     correlation_id, context::text, data::text, seq
                              FROM production_events
                              {where}
                              ORDER BY ingest_id {order}
                              LIMIT @limit;
                              """;
        command.Parameters.AddWithValue("limit", Math.Clamp(query.Limit, 1, 500));

        var events = new List<CentralProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            events.Add(ReadEvent(reader));
        return events;
    }

    public async Task<bool> CanConnectAsync(CancellationToken ct = default)
    {
        try
        {
            await using var command = _dataSource.CreateCommand("SELECT 1;");
            return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
                CultureInfo.InvariantCulture) == 1;
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        return _dataSource.DisposeAsync();
    }

    private static async Task EnsurePartitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset month,
        CancellationToken ct)
    {
        var next = month.AddMonths(1);
        var table = $"production_events_{month:yyyyMM}";
        await using var command = new NpgsqlCommand(
            $"""
             CREATE TABLE IF NOT EXISTS {table}
             PARTITION OF production_events
             FOR VALUES FROM ('{month:yyyy-MM-01T00:00:00Z}')
             TO ('{next:yyyy-MM-01T00:00:00Z}');
             """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long?> GetMaxEdgeSeqAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT MAX(seq) FROM event_ingest_keys WHERE edge_id = @edge_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("edge_id", edgeId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TryReserveEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        ProductionEvent evt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO event_ingest_keys(event_id, edge_id, seq, occurred_at)
            VALUES (@event_id, @edge_id, @seq, @occurred_at)
            ON CONFLICT DO NOTHING
            RETURNING event_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_id", evt.EventId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("seq", evt.Seq);
        command.Parameters.AddWithValue("occurred_at", evt.OccurredAt.UtcDateTime);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        ProductionEvent evt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO production_events(
              event_id, edge_id, seq, event_type, type_version, occurred_at, recorded_at,
              source, subject_type, subject_id, correlation_id, context, data)
            VALUES (
              @event_id, @edge_id, @seq, @event_type, @type_version, @occurred_at, @recorded_at,
              @source, @subject_type, @subject_id, @correlation_id, @context, @data);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_id", evt.EventId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("seq", evt.Seq);
        command.Parameters.AddWithValue("event_type", evt.EventType);
        command.Parameters.AddWithValue("type_version", evt.EventTypeVersion);
        command.Parameters.AddWithValue("occurred_at", evt.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("recorded_at", evt.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("source", evt.Source);
        command.Parameters.AddWithValue("subject_type", evt.Subject.Type);
        command.Parameters.AddWithValue("subject_id", evt.Subject.Id);
        command.Parameters.AddWithValue("correlation_id", (object?)evt.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("context", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Context, JsonOptions));
        command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Data, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task VerifyDuplicateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string edgeId,
        ProductionEvent evt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT event_id, edge_id, seq
            FROM event_ingest_keys
            WHERE event_id = @event_id OR (edge_id = @edge_id AND seq = @seq)
            LIMIT 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_id", evt.EventId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("seq", evt.Seq);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
            !string.Equals(reader.GetString(0), evt.EventId, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), edgeId, StringComparison.OrdinalIgnoreCase) ||
            reader.GetInt64(2) != evt.Seq)
        {
            throw new InvalidDataException(
                $"事件幂等键冲突：EdgeId={edgeId}, Seq={evt.Seq}, EventId={evt.EventId}");
        }
    }

    private static CentralProductionEvent ReadEvent(NpgsqlDataReader reader)
    {
        var context = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(12), JsonOptions)
                      ?? new Dictionary<string, string>();
        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(13), JsonOptions)
                   ?? new Dictionary<string, object?>();
        var evt = new ProductionEvent
        {
            EventId = reader.GetString(3),
            EventType = reader.GetString(4),
            EventTypeVersion = reader.GetInt32(5),
            OccurredAt = new DateTimeOffset(reader.GetDateTime(6).ToUniversalTime()),
            RecordedAt = new DateTimeOffset(reader.GetDateTime(7).ToUniversalTime()),
            Source = reader.GetString(8),
            Subject = new ObjectRef(reader.GetString(9), reader.GetString(10)),
            CorrelationId = reader.IsDBNull(11) ? null : reader.GetString(11),
            Context = context,
            Data = data,
            Seq = reader.GetInt64(14)
        };
        return new CentralProductionEvent
        {
            IngestId = reader.GetInt64(0),
            EdgeId = reader.GetString(1),
            IngestedAt = new DateTimeOffset(reader.GetDateTime(2).ToUniversalTime()),
            Event = evt
        };
    }

    private static void AddEquality(
        NpgsqlCommand command,
        List<string> predicates,
        string column,
        string parameter,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        predicates.Add($"{column} = @{parameter}");
        command.Parameters.AddWithValue(parameter, value.Trim());
    }

    private static DateTimeOffset MonthStart(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    internal static bool HasSequenceGap(
        long? previousMax,
        IReadOnlyList<ProductionEvent> ordered)
    {
        if (ordered.Count == 0)
            return false;

        var baseline = previousMax ?? 0;
        var forward = ordered
            .Where(evt => evt.Seq > baseline)
            .Select(static evt => evt.Seq)
            .ToArray();
        if (forward.Length == 0)
            return false;
        if (forward[0] > baseline + 1)
            return true;

        for (var index = 1; index < forward.Length; index++)
        {
            if (forward[index] > forward[index - 1] + 1)
                return true;
        }

        return false;
    }
}
