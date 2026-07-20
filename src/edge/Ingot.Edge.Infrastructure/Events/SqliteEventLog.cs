using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.Infrastructure.Events;

/// <summary>
///     SQLite outbox：边缘生产事实的本地不可变日志。
/// </summary>
public sealed class SqliteEventLog : IEventLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex ContextKeyPattern = new(
        "^[A-Za-z0-9_.-]{1,128}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly EventOptions _options;
    private readonly ILogger<SqliteEventLog> _logger;
    private readonly IMetricsCollector? _metrics;
    private DateTimeOffset _nextCleanupUtc = DateTimeOffset.MinValue;

    public SqliteEventLog(
        IOptions<EventOptions> options,
        ILogger<SqliteEventLog> logger,
        IMetricsCollector? metrics = null)
    {
        _options = options.Value;
        _logger = logger;
        _metrics = metrics;

        var dbPath = string.IsNullOrWhiteSpace(_options.DatabasePath)
            ? "Data/events.db"
            : _options.DatabasePath;
        if (!Path.IsPathRooted(dbPath))
            dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);

        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        EnsureSchema();
        CleanupAcknowledged();
        ScheduleNextCleanup();
    }

    public async Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(ct)
                .ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO events(
                                    event_id, event_type, type_version, occurred_at, recorded_at,
                                    source, subject_type, subject_id, correlation_id,
                                    context_json, data_json, ship_state, ship_attempts)
                                  VALUES (
                                    $event_id, $event_type, $type_version, $occurred_at, $recorded_at,
                                    $source, $subject_type, $subject_id, $correlation_id,
                                    $context_json, $data_json, 0, 0);
                                  SELECT last_insert_rowid();
                                  """;
            command.Parameters.AddWithValue("$event_id", evt.EventId);
            command.Parameters.AddWithValue("$event_type", evt.EventType);
            command.Parameters.AddWithValue("$type_version", evt.EventTypeVersion);
            command.Parameters.AddWithValue("$occurred_at", evt.OccurredAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$recorded_at", evt.RecordedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$source", evt.Source);
            command.Parameters.AddWithValue("$subject_type", evt.Subject.Type);
            command.Parameters.AddWithValue("$subject_id", evt.Subject.Id);
            command.Parameters.AddWithValue("$correlation_id", (object?)evt.CorrelationId ?? DBNull.Value);
            command.Parameters.AddWithValue("$context_json", JsonSerializer.Serialize(evt.Context, JsonOptions));
            command.Parameters.AddWithValue("$data_json", JsonSerializer.Serialize(evt.Data, JsonOptions));

            var seq = Convert.ToInt64(
                await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            await InsertContextIndexAsync(
                    connection,
                    transaction,
                    seq,
                    evt.Context,
                    ct)
                .ConfigureAwait(false);

            var backlogDrop = await EnforceBacklogLimitAsync(
                    connection,
                    transaction,
                    evt,
                    ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            if (backlogDrop is not null)
            {
                RecordBacklogDropMetrics(backlogDrop.DroppedCount);
                _logger.LogCritical(
                    "事件 outbox 达到硬上限 {MaxBacklogRows}，已显式丢弃最旧的 {Dropped} 条未上行事件，" +
                    "Seq={FirstSeq}-{LastSeq}；审计事件 diagnostic.backlog_dropped 已写入。",
                    _options.MaxBacklogRows,
                    backlogDrop.DroppedCount,
                    backlogDrop.FirstSeq,
                    backlogDrop.LastSeq);
            }

            return seq;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void RecordBacklogDropMetrics(int droppedCount)
    {
        try
        {
            _metrics?.RecordEventBacklogDropped(droppedCount);
            _metrics?.RecordEventEmitted("diagnostic.backlog_dropped", 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "backlog 丢弃事实已经提交，但记录相关指标失败。");
        }
    }

    public async Task<IReadOnlyList<ProductionEvent>> QueryAsync(
        EventQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var predicates = new List<string>();
        var contextJoins = new List<string>();

        AddOptionalEquality(command, predicates, "e.event_type", "$event_type", query.EventType);
        AddOptionalEquality(command, predicates, "e.subject_type", "$subject_type", query.SubjectType);
        AddOptionalEquality(command, predicates, "e.subject_id", "$subject_id", query.SubjectId);
        AddOptionalEquality(command, predicates, "e.correlation_id", "$correlation_id", query.CorrelationId);

        if (query.From.HasValue)
        {
            predicates.Add("e.occurred_at >= $from");
            command.Parameters.AddWithValue("$from", query.From.Value.ToUniversalTime().ToString("O"));
        }

        if (query.To.HasValue)
        {
            predicates.Add("e.occurred_at <= $to");
            command.Parameters.AddWithValue("$to", query.To.Value.ToUniversalTime().ToString("O"));
        }

        if (query.AfterSeq.HasValue)
        {
            predicates.Add("e.seq > $after_seq");
            command.Parameters.AddWithValue("$after_seq", query.AfterSeq.Value);
        }

        var contextIndex = 0;
        foreach (var pair in query.Context)
        {
            if (!ContextKeyPattern.IsMatch(pair.Key))
                throw new ArgumentException($"非法上下文键: {pair.Key}", nameof(query));

            var keyParameter = $"$ctx_key_{contextIndex}";
            var valueParameter = $"$ctx_value_{contextIndex}";
            contextJoins.Add(
                $"""
                 JOIN event_context AS ec{contextIndex}
                   ON ec{contextIndex}.event_seq = e.seq
                  AND ec{contextIndex}.ctx_key = {keyParameter}
                  AND ec{contextIndex}.ctx_value = {valueParameter}
                 """);
            command.Parameters.AddWithValue(keyParameter, pair.Key);
            command.Parameters.AddWithValue(valueParameter, pair.Value);
            contextIndex++;
        }

        var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
        var joins = string.Join(Environment.NewLine, contextJoins);
        var order = query.AfterSeq.HasValue ? "ASC" : "DESC";
        command.CommandText = $"""
                               SELECT e.seq, e.event_id, e.event_type, e.type_version, e.occurred_at, e.recorded_at,
                                      e.source, e.subject_type, e.subject_id, e.correlation_id, e.context_json, e.data_json
                               FROM events AS e
                               {joins}
                               {where}
                               ORDER BY e.seq {order}
                               LIMIT $limit;
                               """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 500));

        var events = new List<ProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            events.Add(ReadEvent(reader));

        return events;
    }

    public Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(int max, CancellationToken ct = default)
        => QueryPendingAsync(Math.Clamp(max, 1, 500), ct);

    public async Task MarkShippedAsync(long upToSeq, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  UPDATE events
                                  SET ship_state = 1
                                  WHERE seq <= $seq AND ship_state = 0;
                                  """;
            command.Parameters.AddWithValue("$seq", upToSeq);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await CleanupAcknowledgedIfDueAsync(connection, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task IncrementShipAttemptsAsync(
        long fromSeq,
        long toSeq,
        CancellationToken ct = default)
    {
        if (fromSeq <= 0 || toSeq < fromSeq)
            throw new ArgumentOutOfRangeException(nameof(fromSeq), "上行尝试的 Seq 范围无效。");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  UPDATE events
                                  SET ship_attempts = ship_attempts + 1
                                  WHERE seq BETWEEN $from_seq AND $to_seq
                                    AND ship_state = 0;
                                  """;
            command.Parameters.AddWithValue("$from_seq", fromSeq);
            command.Parameters.AddWithValue("$to_seq", toSeq);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<long> CountPendingAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events WHERE ship_state = 0;";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<ProductionEvent>> QueryPendingAsync(int max, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT seq, event_id, event_type, type_version, occurred_at, recorded_at,
                                     source, subject_type, subject_id, correlation_id, context_json, data_json
                              FROM events
                              WHERE ship_state = 0
                              ORDER BY seq ASC
                              LIMIT $limit;
                              """;
        command.Parameters.AddWithValue("$limit", max);

        var events = new List<ProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            events.Add(ReadEvent(reader));
        return events;
    }

    private async Task<BacklogDrop?> EnforceBacklogLimitAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionEvent cause,
        CancellationToken ct)
    {
        if (_options.MaxBacklogRows <= 0)
            return null;

        await using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT COUNT(*) FROM events WHERE ship_state = 0;";
        var count = Convert.ToInt64(
            await countCommand.ExecuteScalarAsync(ct).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        var excess = count - _options.MaxBacklogRows;
        if (excess <= 0)
            return null;

        // 审计事件本身也必须占用一个受控的 outbox 槽位，因此比单纯超额数多释放一行。
        var rowsToDrop = Math.Min(count, excess + 1);
        await using var rangeCommand = connection.CreateCommand();
        rangeCommand.Transaction = transaction;
        rangeCommand.CommandText = """
                                   SELECT MIN(seq), MAX(seq)
                                   FROM (
                                     SELECT seq
                                     FROM events
                                     WHERE ship_state = 0
                                     ORDER BY seq ASC
                                     LIMIT $rows_to_drop
                                   );
                                   """;
        rangeCommand.Parameters.AddWithValue("$rows_to_drop", rowsToDrop);
        await using var rangeReader = await rangeCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rangeReader.ReadAsync(ct).ConfigureAwait(false) || rangeReader.IsDBNull(0))
            return null;
        var firstSeq = rangeReader.GetInt64(0);
        var lastSeq = rangeReader.GetInt64(1);
        await rangeReader.DisposeAsync().ConfigureAwait(false);

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = """
                                    DELETE FROM events
                                    WHERE seq IN (
                                      SELECT seq FROM events
                                      WHERE ship_state = 0
                                      ORDER BY seq ASC
                                      LIMIT $rows_to_drop
                                    );
                                    """;
        deleteCommand.Parameters.AddWithValue("$rows_to_drop", rowsToDrop);
        var dropped = await deleteCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var diagnosticData = new Dictionary<string, object?>
        {
            ["reason"] = "max_backlog_rows_exceeded",
            ["dropped_count"] = dropped,
            ["dropped_seq_from"] = firstSeq,
            ["dropped_seq_to"] = lastSeq,
            ["max_backlog_rows"] = _options.MaxBacklogRows
        };
        await using var diagnosticCommand = connection.CreateCommand();
        diagnosticCommand.Transaction = transaction;
        diagnosticCommand.CommandText = """
                                        INSERT INTO events(
                                          event_id, event_type, type_version, occurred_at, recorded_at,
                                          source, subject_type, subject_id, correlation_id,
                                          context_json, data_json, ship_state, ship_attempts)
                                        VALUES (
                                          $event_id, 'diagnostic.backlog_dropped', 1, $occurred_at, $recorded_at,
                                          $source, 'system', 'event-outbox', NULL,
                                          '{}', $data_json, 0, 0);
                                        """;
        diagnosticCommand.Parameters.AddWithValue("$event_id", Guid.CreateVersion7().ToString());
        diagnosticCommand.Parameters.AddWithValue("$occurred_at", now.ToString("O"));
        diagnosticCommand.Parameters.AddWithValue("$recorded_at", now.ToString("O"));
        diagnosticCommand.Parameters.AddWithValue("$source", BuildBacklogDiagnosticSource(cause.Source));
        diagnosticCommand.Parameters.AddWithValue(
            "$data_json",
            JsonSerializer.Serialize(diagnosticData, JsonOptions));
        await diagnosticCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        return new BacklogDrop(dropped, firstSeq, lastSeq);
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS events (
                                seq            INTEGER PRIMARY KEY AUTOINCREMENT,
                                event_id       TEXT NOT NULL UNIQUE,
                                event_type     TEXT NOT NULL,
                                type_version   INTEGER NOT NULL DEFAULT 1,
                                occurred_at    TEXT NOT NULL,
                                recorded_at    TEXT NOT NULL,
                                source         TEXT NOT NULL,
                                subject_type   TEXT NOT NULL,
                                subject_id     TEXT NOT NULL,
                                correlation_id TEXT,
                                context_json   TEXT NOT NULL DEFAULT '{}',
                                data_json      TEXT NOT NULL DEFAULT '{}',
                                ship_state     INTEGER NOT NULL DEFAULT 0,
                                ship_attempts  INTEGER NOT NULL DEFAULT 0
                              );
                              CREATE INDEX IF NOT EXISTS idx_events_type_time
                                ON events(event_type, occurred_at);
                              CREATE INDEX IF NOT EXISTS idx_events_subject_time
                                ON events(subject_type, subject_id, occurred_at);
                              CREATE INDEX IF NOT EXISTS idx_events_correlation
                                ON events(correlation_id, seq);
                              CREATE INDEX IF NOT EXISTS idx_events_ship
                                ON events(ship_state, seq);
                              CREATE TABLE IF NOT EXISTS event_context (
                                event_seq INTEGER NOT NULL
                                  REFERENCES events(seq) ON DELETE CASCADE,
                                ctx_key   TEXT NOT NULL,
                                ctx_value TEXT NOT NULL,
                                PRIMARY KEY(event_seq, ctx_key)
                              );
                              CREATE INDEX IF NOT EXISTS idx_event_context_lookup
                                ON event_context(ctx_key, ctx_value, event_seq);
                              INSERT OR IGNORE INTO event_context(event_seq, ctx_key, ctx_value)
                              SELECT events.seq, json_each.key, CAST(json_each.value AS TEXT)
                              FROM events, json_each(events.context_json);
                              """;
        command.ExecuteNonQuery();
    }

    private void CleanupAcknowledged()
    {
        if (_options.RetentionDays <= 0)
            return;

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              DELETE FROM events
                              WHERE ship_state = 1 AND recorded_at < $cutoff;
                              """;
        command.Parameters.AddWithValue(
            "$cutoff",
            DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays).ToString("O"));
        var removed = command.ExecuteNonQuery();
        if (removed > 0)
            _logger.LogInformation("已按保留策略清理 {Removed} 条中心已确认事件", removed);
    }

    private async Task CleanupAcknowledgedIfDueAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        if (_options.RetentionDays <= 0 || DateTimeOffset.UtcNow < _nextCleanupUtc)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              DELETE FROM events
                              WHERE ship_state = 1 AND recorded_at < $cutoff;
                              """;
        command.Parameters.AddWithValue(
            "$cutoff",
            DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays).ToString("O"));
        var removed = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        ScheduleNextCleanup();
        if (removed > 0)
            _logger.LogInformation("已按保留策略清理 {Removed} 条中心已确认事件", removed);
    }

    private void ScheduleNextCleanup()
    {
        var seconds = Math.Max(0, _options.CleanupIntervalSeconds);
        _nextCleanupUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText =
            "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText =
            "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static void AddOptionalEquality(
        SqliteCommand command,
        ICollection<string> predicates,
        string column,
        string parameter,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        predicates.Add($"{column} = {parameter}");
        command.Parameters.AddWithValue(parameter, value.Trim());
    }

    private static async Task InsertContextIndexAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long seq,
        IReadOnlyDictionary<string, string> context,
        CancellationToken ct)
    {
        foreach (var pair in context)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO event_context(event_seq, ctx_key, ctx_value)
                                  VALUES ($event_seq, $ctx_key, $ctx_value);
                                  """;
            command.Parameters.AddWithValue("$event_seq", seq);
            command.Parameters.AddWithValue("$ctx_key", pair.Key);
            command.Parameters.AddWithValue("$ctx_value", pair.Value);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static ProductionEvent ReadEvent(SqliteDataReader reader)
    {
        return new ProductionEvent
        {
            Seq = reader.GetInt64(0),
            EventId = reader.GetString(1),
            EventType = reader.GetString(2),
            EventTypeVersion = reader.GetInt32(3),
            OccurredAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            RecordedAt = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            Source = reader.GetString(6),
            Subject = new ObjectRef(reader.GetString(7), reader.GetString(8)),
            CorrelationId = reader.IsDBNull(9) ? null : reader.GetString(9),
            Context = DeserializeContext(reader.GetString(10)),
            Data = DeserializeData(reader.GetString(11))
        };
    }

    private static IReadOnlyDictionary<string, string> DeserializeContext(string json)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
           ?? new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, object?> DeserializeData(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions)
                     ?? new Dictionary<string, JsonElement>();
        return values.ToDictionary(
            static pair => pair.Key,
            static pair => (object?)pair.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static string BuildBacklogDiagnosticSource(string source)
    {
        var segments = source.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2 &&
               string.Equals(segments[0], "edge", StringComparison.OrdinalIgnoreCase)
            ? $"edge/{segments[1]}/system/event-outbox"
            : "edge/local/system/event-outbox";
    }

    private sealed record BacklogDrop(int DroppedCount, long FirstSeq, long LastSeq);
}
