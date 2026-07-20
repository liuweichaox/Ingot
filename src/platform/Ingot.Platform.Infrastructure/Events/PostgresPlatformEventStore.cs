using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Events;

/// <summary>
///     TimescaleDB（PostgreSQL + 时序扩展）中心数据存储。全局去重键表与事件 hypertable 分离，
///     保证 EventId、(EdgeId, Seq) 幂等；记录表由 Timescale 按 occurred_at 自动分块，
///     并可按配置启用保留与压缩策略。
/// </summary>
public sealed partial class PostgresPlatformEventStore : IPlatformEventStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresPlatformEventStore> _logger;
    private readonly PlatformEventMetrics _metrics;
    private readonly PlatformEventOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    // Postgres INTERVAL 字面量白名单：配置为可信来源，仍做防御式校验后才内联进 DDL。
    [GeneratedRegex(@"^\d+\s+(second|minute|hour|day|week|month|year)s?$", RegexOptions.IgnoreCase)]
    private static partial Regex IntervalPattern();

    public PostgresPlatformEventStore(
        IConfiguration configuration,
        ILogger<PostgresPlatformEventStore> logger,
        PlatformEventMetrics metrics,
        IOptions<PlatformEventOptions> options)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
        _metrics = metrics;
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

            // 注意：production_events 不再使用原生 PARTITION BY，改由 Timescale hypertable 自动分块。
            // 幂等去重仍由独立的 event_ingest_keys（普通表、带唯一约束）承担，不受 hypertable 约束影响。
            await using var command = _dataSource.CreateCommand(
                """
                CREATE EXTENSION IF NOT EXISTS timescaledb;

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
                );

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

            await ConfigureHypertableAsync(ct).ConfigureAwait(false);

            _initialized = true;
            _logger.LogInformation("TimescaleDB 中心事件库结构已就绪");
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

        // 无需在热路径创建分区：Timescale hypertable 在插入时自动落到对应时间块。
        // OccurredAt 的合理区间由上游 PlatformIngestWindow 校验，防止异常时间戳撑出无意义的远期块。
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

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

    public async Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        PlatformEventQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand();
        var where = BuildWhere(command, query);
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

        var events = new List<PlatformProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            events.Add(ReadEvent(reader));
        return events;
    }

    public async Task<PlatformEventScopeStats> GetScopeStatsAsync(
        PlatformEventQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand();
        var where = BuildWhere(command, query);
        // 全范围聚合，不受 Limit 截断；hypertable 上 max/min(occurred_at) 与 count 都能借助时间维索引与块裁剪。
        command.CommandText = $"""
                              SELECT count(*), max(occurred_at), min(occurred_at)
                              FROM production_events
                              {where};
                              """;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return new PlatformEventScopeStats();
        return new PlatformEventScopeStats
        {
            Count = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            LatestOccurredAt = reader.IsDBNull(1)
                ? null
                : new DateTimeOffset(reader.GetDateTime(1).ToUniversalTime()),
            EarliestOccurredAt = reader.IsDBNull(2)
                ? null
                : new DateTimeOffset(reader.GetDateTime(2).ToUniversalTime())
        };
    }

    // 按查询条件构造 WHERE 子句并绑定参数（QueryAsync 与 GetScopeStatsAsync 共用，保证筛选规则一致）。
    private static string BuildWhere(NpgsqlCommand command, PlatformEventQuery query)
    {
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
                throw new ArgumentException($"非法生产信息项: {pair.Key}", nameof(query));
            var keyName = $"ctx_key_{contextIndex}";
            var valueName = $"ctx_value_{contextIndex}";
            predicates.Add($"context ->> @{keyName} = @{valueName}");
            command.Parameters.AddWithValue(keyName, pair.Key);
            command.Parameters.AddWithValue(valueName, pair.Value);
            contextIndex++;
        }

        return predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
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

    // 把 production_events 变为 hypertable，并按配置注册保留 / 压缩策略。幂等：重复调用安全。
    private async Task ConfigureHypertableAsync(CancellationToken ct)
    {
        var chunkInterval = IntervalPattern().IsMatch(_options.ChunkTimeInterval.Trim())
            ? _options.ChunkTimeInterval.Trim()
            : "30 days";
        if (!string.Equals(chunkInterval, _options.ChunkTimeInterval.Trim(), StringComparison.Ordinal))
            _logger.LogWarning(
                "无效的 ChunkTimeInterval='{Configured}'，回退为 '30 days'。",
                _options.ChunkTimeInterval);

        // migrate_data 允许在已有数据的表上首次转 hypertable；已是 hypertable 时 if_not_exists 直接跳过。
        await using (var hypertable = _dataSource.CreateCommand(
            $"SELECT create_hypertable('production_events', 'occurred_at', "
            + $"chunk_time_interval => INTERVAL '{chunkInterval}', if_not_exists => TRUE, migrate_data => TRUE);"))
        {
            await hypertable.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        if (_options.CompressAfterDays > 0)
        {
            await using var compress = _dataSource.CreateCommand(
                "ALTER TABLE production_events SET ("
                + "timescaledb.compress, "
                + "timescaledb.compress_segmentby = 'edge_id, subject_type, subject_id', "
                + "timescaledb.compress_orderby = 'occurred_at DESC');"
                + $"SELECT add_compression_policy('production_events', INTERVAL '{_options.CompressAfterDays} days', if_not_exists => TRUE);");
            await compress.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }

        if (_options.RetentionDays > 0)
        {
            await using var retention = _dataSource.CreateCommand(
                $"SELECT add_retention_policy('production_events', INTERVAL '{_options.RetentionDays} days', if_not_exists => TRUE);");
            await retention.ExecuteScalarAsync(ct).ConfigureAwait(false);
        }
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

    private static PlatformProductionEvent ReadEvent(NpgsqlDataReader reader)
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
        return new PlatformProductionEvent
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
