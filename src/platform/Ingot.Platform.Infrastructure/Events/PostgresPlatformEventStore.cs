using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
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
    private readonly IManufacturingContextStore _manufacturingContexts;
    private readonly ProcessAnalysisResolver _analysisResolver;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    // Postgres INTERVAL 字面量白名单：配置为可信来源，仍做防御式校验后才内联进 DDL。
    [GeneratedRegex(@"^\d+\s+(second|minute|hour|day|week|month|year)s?$", RegexOptions.IgnoreCase)]
    private static partial Regex IntervalPattern();

    public PostgresPlatformEventStore(
        IConfiguration configuration,
        ILogger<PostgresPlatformEventStore> logger,
        PlatformEventMetrics metrics,
        IOptions<PlatformEventOptions> options,
        IManufacturingContextStore manufacturingContexts,
        ProcessAnalysisResolver analysisResolver)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
        _metrics = metrics;
        _options = options.Value;
        _manufacturingContexts = manufacturingContexts;
        _analysisResolver = analysisResolver;
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

                CREATE TABLE IF NOT EXISTS operation_context_snapshots (
                  correlation_id    TEXT PRIMARY KEY,
                  subject_type      TEXT NOT NULL,
                  subject_id        TEXT NOT NULL,
                  started_event_type TEXT NOT NULL,
                  captured_at       TIMESTAMPTZ NOT NULL,
                  context           JSONB NOT NULL
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

        // 在运行开始时解析一次不可变上下文，并传播到同一 correlationId 的全部后续事件。
        // 这样完成事件、质量任务与每秒样本看到的是同一份产品、配方和工装快照。
        var capturedContexts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var analysisConfigurations = new Dictionary<string, ResolvedProcessAnalysis?>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            ordered[index] = await EnrichOperationContextAsync(ordered[index], capturedContexts, ct).ConfigureAwait(false);
            await ValidateProcessSampleAsync(ordered[index], analysisConfigurations, ct).ConfigureAwait(false);
        }

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
            if (evt.EventType.EndsWith(".started", StringComparison.Ordinal))
            {
                await UpsertOperationContextSnapshotAsync(connection, transaction, evt, ct)
                    .ConfigureAwait(false);
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

    private async Task<ProductionEvent> EnrichOperationContextAsync(
        ProductionEvent evt,
        IDictionary<string, IReadOnlyDictionary<string, string>> capturedContexts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.CorrelationId))
            return evt;

        var correlationId = evt.CorrelationId;
        var isStart = evt.EventType.EndsWith(".started", StringComparison.Ordinal);
        if (!isStart)
        {
            if (!capturedContexts.TryGetValue(correlationId, out var captured))
            {
                captured = await LoadOperationContextSnapshotAsync(correlationId, ct).ConfigureAwait(false);
                if (captured is null)
                {
                    // Compatibility for event stores created before persistent operation snapshots.
                    var previous = await QueryAsync(new PlatformEventQuery
                    {
                        CorrelationId = correlationId,
                        EventType = "cycle.started",
                        Limit = 1
                    }, ct).ConfigureAwait(false);
                    captured = previous.FirstOrDefault()?.Event.Context;
                }
                if (captured is not null)
                    capturedContexts[correlationId] = captured;
            }
            return captured is null ? evt : evt with { Context = MergeCapturedContext(captured, evt.Context) };
        }

        var resolved = await _manufacturingContexts.ResolveAsync(evt.Subject.Id, evt.OccurredAt, ct)
            .ConfigureAwait(false);
        var context = new Dictionary<string, string>(evt.Context, StringComparer.Ordinal);
        if (resolved is not null)
        {
            context["production_context_id"] = resolved.Production.ContextId.ToString("D");
            context["product_series"] = resolved.Production.ProductSeries;
            context["product_code"] = resolved.Production.ProductCode;
            context["recipe_id"] = resolved.Production.RecipeId;
            context["recipe_version"] = resolved.Production.RecipeVersion;
            context["tooling_installation_id"] = resolved.Installation.InstallationId.ToString("D");
            context["tooling_id"] = resolved.Assembly.MoldId;
            context["mold_id"] = resolved.Assembly.MoldId;
            context["assembly_revision_id"] = resolved.AssemblyRevision.AssemblyRevisionId.ToString("D");
            context["assembly_revision"] = resolved.AssemblyRevision.Revision.ToString(CultureInfo.InvariantCulture);
            context["context_captured_at"] = evt.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
            context["context_capture_status"] = "resolved";
            AddContext(context, "external_order_ref", resolved.Production.ExternalOrderRef);
            AddContext(context, "external_batch_ref", resolved.Production.ExternalBatchRef);
            AddContext(context, "material_lot_ref", resolved.Production.MaterialLotRef);
        }
        else
        {
            context["context_captured_at"] = evt.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
            context["context_capture_status"] = "configuration_missing";
        }

        var recipe = await _analysisResolver.ResolveRecipeAsync(context, ct).ConfigureAwait(false);
        var analysisScope = evt.EventType.StartsWith("run.", StringComparison.Ordinal)
            ? "production-run"
            : "production-cycle";
        var analysis = await _analysisResolver.ResolveAsync(context, analysisScope, ct).ConfigureAwait(false);
        if (recipe is not null)
        {
            context["data_model_id"] = recipe.DataModelId;
            context["data_model_version"] = recipe.DataModelVersion.ToString(CultureInfo.InvariantCulture);
            context["recipe_snapshot_status"] = "resolved";
        }
        if (analysis is not null)
        {
            context["analysis_plan_id"] = analysis.Plan.PlanId;
            context["analysis_plan_version"] = analysis.Plan.Version.ToString(CultureInfo.InvariantCulture);
        }

        var data = new Dictionary<string, object?>(evt.Data, StringComparer.Ordinal);
        if (recipe is not null)
        {
            data["recipeParameters"] = recipe.Values.ToDictionary(
                static item => item.Code,
                static item => (object?)item.Value,
                StringComparer.Ordinal);
        }
        capturedContexts[correlationId] = context;
        return evt with { Context = context, Data = data };
    }

    private async Task<IReadOnlyDictionary<string, string>?> LoadOperationContextSnapshotAsync(
        string correlationId,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT context::text FROM operation_context_snapshots WHERE correlation_id = @correlation_id;");
        command.Parameters.AddWithValue("correlation_id", correlationId);
        var raw = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return string.IsNullOrWhiteSpace(raw)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonOptions);
    }

    private static async Task UpsertOperationContextSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProductionEvent evt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO operation_context_snapshots(
              correlation_id, subject_type, subject_id, started_event_type, captured_at, context)
            VALUES (@correlation_id, @subject_type, @subject_id, @started_event_type, @captured_at, @context)
            ON CONFLICT (correlation_id) DO UPDATE SET
              subject_type = EXCLUDED.subject_type,
              subject_id = EXCLUDED.subject_id,
              started_event_type = EXCLUDED.started_event_type,
              captured_at = EXCLUDED.captured_at,
              context = EXCLUDED.context;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("correlation_id", evt.CorrelationId!);
        command.Parameters.AddWithValue("subject_type", evt.Subject.Type);
        command.Parameters.AddWithValue("subject_id", evt.Subject.Id);
        command.Parameters.AddWithValue("started_event_type", evt.EventType);
        command.Parameters.AddWithValue("captured_at", evt.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("context", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Context, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> MergeCapturedContext(
        IReadOnlyDictionary<string, string> captured,
        IReadOnlyDictionary<string, string> current)
    {
        var result = new Dictionary<string, string>(captured, StringComparer.Ordinal);
        foreach (var pair in current)
        {
            if (pair.Key is "recipe_step" or "recipe_step_name" or "process_phase" or "process_stage" ||
                !result.ContainsKey(pair.Key))
                result[pair.Key] = pair.Value;
        }
        return result;
    }

    private async Task ValidateProcessSampleAsync(
        ProductionEvent evt,
        IDictionary<string, ResolvedProcessAnalysis?> configurations,
        CancellationToken ct)
    {
        if (!string.Equals(evt.EventType, "process.sample", StringComparison.Ordinal))
            return;
        var cacheKey = evt.CorrelationId ?? $"{evt.Subject.Type}:{evt.Subject.Id}";
        if (!configurations.TryGetValue(cacheKey, out var analysis))
        {
            analysis = await _analysisResolver.ResolveAsync(evt.Context, "production-cycle", ct).ConfigureAwait(false)
                       ?? await _analysisResolver.ResolveAsync(evt.Context, "production-run", ct).ConfigureAwait(false)
                       ?? await _analysisResolver.ResolveAsync(evt.Context, "analysis-window", ct).ConfigureAwait(false);
            configurations[cacheKey] = analysis;
        }
        if (analysis is null)
            return;
        if (!evt.Data.TryGetValue("values", out var rawValues) || !TryReadObject(rawValues, out var values))
            throw new ArgumentException($"事件 {evt.EventId} 的 process.sample.data.values 必须是对象。");

        var definitions = analysis.DataModel.Acquisition.DataItems.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var unknown = values.Keys.FirstOrDefault(key => !definitions.ContainsKey(key));
        if (unknown is not null)
            throw new ArgumentException($"事件 {evt.EventId} 包含工艺数据模型未定义的数据项：{unknown}。");
        var missing = definitions.Values.FirstOrDefault(item => !item.Nullable && !values.ContainsKey(item.Code));
        if (missing is not null)
            throw new ArgumentException($"事件 {evt.EventId} 缺少必填采集数据项：{missing.Code}。");
        foreach (var pair in values)
        {
            if (!ValueMatchesType(pair.Value, definitions[pair.Key].DataType))
                throw new ArgumentException($"事件 {evt.EventId} 的数据项 {pair.Key} 类型不符合 {definitions[pair.Key].DataType}。");
        }
    }

    private static bool TryReadObject(object? raw, out IReadOnlyDictionary<string, object?> values)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            values = element.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => (object?)property.Value,
                StringComparer.Ordinal);
            return true;
        }
        if (raw is IReadOnlyDictionary<string, object?> readOnly)
        {
            values = readOnly;
            return true;
        }
        if (raw is IDictionary<string, object?> dictionary)
        {
            values = dictionary.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            return true;
        }
        values = new Dictionary<string, object?>();
        return false;
    }

    private static bool ValueMatchesType(object? raw, string dataType)
    {
        if (raw is null || raw is JsonElement { ValueKind: JsonValueKind.Null })
            return true;
        if (raw is JsonElement element)
        {
            return dataType switch
            {
                "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out _),
                "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "string" => element.ValueKind == JsonValueKind.String,
                _ => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out _)
            };
        }
        return dataType switch
        {
            "integer" => raw is sbyte or byte or short or ushort or int or uint or long or ulong,
            "boolean" => raw is bool,
            "string" => raw is string,
            _ => raw is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
        };
    }

    private static void AddContext(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[key] = value;
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
                              LIMIT @limit
                              OFFSET @offset;
                              """;
        command.Parameters.AddWithValue("limit", Math.Clamp(query.Limit, 1, 500));
        command.Parameters.AddWithValue("offset", query.Offset);

        var events = new List<PlatformProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            events.Add(ReadEvent(reader));
        return events;
    }

    public async Task<IReadOnlyList<PlatformProductionEvent>> QueryByCorrelationIdsAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(correlationIds);
        await InitializeAsync(ct).ConfigureAwait(false);
        var ids = correlationIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return [];

        await using var command = _dataSource.CreateCommand(
            """
            SELECT ingest_id, edge_id, ingested_at, event_id, event_type, type_version,
                   occurred_at, recorded_at, source, subject_type, subject_id,
                   correlation_id, context::text, data::text, seq
            FROM production_events
            WHERE correlation_id = ANY(@correlation_ids)
            ORDER BY correlation_id, occurred_at, ingest_id;
            """);
        command.Parameters.AddWithValue("correlation_ids", ids);
        var result = new List<PlatformProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadEvent(reader));
        return result;
    }

    public async Task<DataObjectPage> QueryDataObjectsAsync(
        DataObjectQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        await using var command = _dataSource.CreateCommand();
        var predicates = new List<string>();
        AddEquality(command, predicates, "subject_type", "subject_type", query.SubjectType);
        AddEquality(command, predicates, "subject_id", "subject_id", query.SubjectId);
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
        var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
        command.CommandText = $"""
                              WITH filtered AS (
                                SELECT ingest_id, edge_id, event_type, occurred_at, subject_type,
                                       subject_id, correlation_id, context
                                FROM production_events
                                {where}
                              ),
                              aggregate_rows AS (
                                SELECT subject_type, subject_id,
                                       count(*) AS event_count,
                                       count(*) FILTER (WHERE event_type = 'process.sample') AS sample_count,
                                       count(DISTINCT correlation_id) AS operation_count,
                                       min(occurred_at) AS first_observed_at,
                                       max(occurred_at) AS last_observed_at,
                                       max(occurred_at) FILTER (WHERE event_type = 'process.sample') AS last_sample_at
                                FROM filtered
                                GROUP BY subject_type, subject_id
                              ),
                              latest_rows AS (
                                SELECT DISTINCT ON (subject_type, subject_id)
                                       subject_type, subject_id, edge_id, event_type, context
                                FROM filtered
                                ORDER BY subject_type, subject_id, occurred_at DESC, ingest_id DESC
                              ),
                              sample_intervals AS (
                                SELECT subject_type, subject_id,
                                       EXTRACT(EPOCH FROM occurred_at - lag(occurred_at) OVER (
                                         PARTITION BY subject_type, subject_id ORDER BY occurred_at, ingest_id
                                       )) AS gap_seconds
                                FROM filtered
                                WHERE event_type = 'process.sample'
                              ),
                              gap_rows AS (
                                SELECT subject_type, subject_id, max(gap_seconds) AS maximum_sample_gap_seconds
                                FROM sample_intervals
                                GROUP BY subject_type, subject_id
                              )
                              SELECT aggregate_rows.subject_type, aggregate_rows.subject_id,
                                     latest_rows.edge_id, aggregate_rows.event_count,
                                     aggregate_rows.sample_count, aggregate_rows.operation_count,
                                     aggregate_rows.first_observed_at, aggregate_rows.last_observed_at,
                                     aggregate_rows.last_sample_at, gap_rows.maximum_sample_gap_seconds,
                                     latest_rows.event_type, latest_rows.context::text,
                                     count(*) OVER() AS total_count
                              FROM aggregate_rows
                              JOIN latest_rows USING (subject_type, subject_id)
                              LEFT JOIN gap_rows USING (subject_type, subject_id)
                              ORDER BY aggregate_rows.last_observed_at DESC,
                                       aggregate_rows.subject_type, aggregate_rows.subject_id
                              LIMIT @limit OFFSET @offset;
                              """;
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);
        var rows = new List<DataObjectSummary>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            total = checked((int)reader.GetInt64(12));
            rows.Add(new DataObjectSummary
            {
                SubjectType = reader.GetString(0),
                SubjectId = reader.GetString(1),
                EdgeId = reader.IsDBNull(2) ? null : reader.GetString(2),
                EventCount = reader.GetInt64(3),
                SampleCount = reader.GetInt64(4),
                OperationCount = reader.GetInt64(5),
                FirstObservedAt = ReadTimestamp(reader, 6),
                LastObservedAt = ReadTimestamp(reader, 7),
                LastSampleAt = ReadTimestamp(reader, 8),
                MaximumSampleGapSeconds = reader.IsDBNull(9)
                    ? null
                    : Convert.ToDouble(reader.GetValue(9), CultureInfo.InvariantCulture),
                LatestEventType = reader.IsDBNull(10) ? null : reader.GetString(10),
                Context = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(11), JsonOptions)
                          ?? new Dictionary<string, string>(StringComparer.Ordinal)
            });
        }
        return new DataObjectPage
        {
            Data = rows,
            Total = total,
            Limit = limit,
            Offset = offset
        };
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
        if (query.BeforeIngestId.HasValue)
        {
            predicates.Add("ingest_id < @before_ingest_id");
            command.Parameters.AddWithValue("before_ingest_id", query.BeforeIngestId.Value);
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

    private static DateTimeOffset? ReadTimestamp(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : new DateTimeOffset(reader.GetDateTime(ordinal).ToUniversalTime());

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
