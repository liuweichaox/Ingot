using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Events;

/// <summary>
///     TimescaleDB（PostgreSQL + 时序扩展）中心数据存储。全局去重键表与事件 hypertable 分离，
///     保证 EventId、(SiteId, EdgeId, Seq) 幂等；记录表由 Timescale 按 occurred_at 自动分块，
///     并可按配置启用保留与压缩策略。
/// </summary>
public sealed partial class PostgresPlatformEventStore : IPlatformEventStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresPlatformEventStore> _logger;
    private readonly PlatformEventMetrics _metrics;
    private readonly IManufacturingContextStore _manufacturingContexts;
    private readonly ProcessAnalysisResolver _analysisResolver;
    private readonly IProcessExecutionAnalysisMaterializationStore _analysisMaterializations;
    private readonly PostgresTimeSeriesStore _timeSeriesStore;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresPlatformEventStore(
        NpgsqlDataSource dataSource,
        ILogger<PostgresPlatformEventStore> logger,
        PlatformEventMetrics metrics,
        IOptions<PlatformEventOptions> options,
        IManufacturingContextStore manufacturingContexts,
        ProcessAnalysisResolver analysisResolver,
        IProcessExecutionAnalysisMaterializationStore analysisMaterializations,
        PostgresTimeSeriesStore timeSeriesStore)
    {
        _dataSource = dataSource;
        _logger = logger;
        _metrics = metrics;
        _manufacturingContexts = manufacturingContexts;
        _analysisResolver = analysisResolver;
        _analysisMaterializations = analysisMaterializations;
        _timeSeriesStore = timeSeriesStore;
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

            await _timeSeriesStore.InitializeAsync(ct).ConfigureAwait(false);
            await using var topology = _dataSource.CreateCommand(
                """
                SELECT EXISTS (
                  SELECT 1 FROM timescaledb_information.hypertables
                  WHERE hypertable_schema = current_schema()
                    AND hypertable_name = 'production_events')
                """);
            if (await topology.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                throw new InvalidOperationException(
                    "生产事件 TimescaleDB 拓扑不存在；请先运行版本化数据库迁移。");

            _initialized = true;
            _logger.LogInformation("TimescaleDB 中心事件存储拓扑已就绪");
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
        for (var index = 0; index < ordered.Length; index++)
        {
            if (!ProductionEventValidator.TryValidate(
                    ordered[index],
                    requirePersistedSequence: true,
                    out var validationError))
            {
                throw new ArgumentException(
                    $"Events[{index}] 无效：{validationError}",
                    nameof(request));
            }
        }
        var sourcePayloadHashes = ordered.ToDictionary(
            static evt => evt.EventId,
            static evt => evt.PayloadHash,
            StringComparer.Ordinal);

        // 在运行开始时解析一次不可变上下文，并传播到同一 executionId 的全部后续事件。
        // 这样完成事件、质量任务与每秒样本看到的是同一份产品、工艺规范和工装快照。
        var capturedContexts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var analysisConfigurations = new Dictionary<string, ResolvedProcessAnalysis?>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            ordered[index] = ProductionEventIntegrity.Seal(
                await EnrichOperationContextAsync(ordered[index], capturedContexts, ct).ConfigureAwait(false));
            await ValidateProcessSampleAsync(ordered[index], analysisConfigurations, ct).ConfigureAwait(false);
        }

        // 无需在热路径创建分区：Timescale hypertable 在插入时自动落到对应时间块。
        // OccurredAt 的合理区间由上游 PlatformIngestWindow 校验，防止异常时间戳撑出无意义的远期块。
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var previousMax = await GetMaxEdgeSeqAsync(
                connection, transaction, request.SiteId, request.EdgeId, ct)
            .ConfigureAwait(false);
        var gapDetected = HasSequenceGap(previousMax, ordered);
        var accepted = 0;
        var duplicates = 0;
        var acceptedMaxIngestByExecutionId = new Dictionary<string, long>(StringComparer.Ordinal);
        var transactionContexts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var evt in ordered)
        {
            var sourcePayloadHash = sourcePayloadHashes[evt.EventId];
            if (await TryReserveEventAsync(
                    connection, transaction, request.SiteId, request.EdgeId, evt, sourcePayloadHash, ct)
                    .ConfigureAwait(false))
            {
                var effectiveEvent = evt;
                if (evt.EventType.EndsWith(".started", StringComparison.Ordinal))
                {
                    effectiveEvent = await AttachToolingUsageOrdinalAsync(
                        connection,
                        transaction,
                        evt,
                        ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(effectiveEvent.ExecutionId))
                        transactionContexts[effectiveEvent.ExecutionId] = effectiveEvent.Context;
                }
                else if (!string.IsNullOrWhiteSpace(evt.ExecutionId) &&
                         transactionContexts.TryGetValue(evt.ExecutionId, out var captured))
                {
                    effectiveEvent = evt with { Context = MergeCapturedContext(captured, evt.Context) };
                }
                effectiveEvent = ProductionEventIntegrity.Seal(effectiveEvent);

                var isProcessSample = string.Equals(
                    effectiveEvent.EventType,
                    "process.sample",
                    StringComparison.Ordinal);
                var ingestedAt = DateTimeOffset.UtcNow;
                var ingestId = isProcessSample
                    ? await AllocateIngestIdAsync(connection, transaction, ct).ConfigureAwait(false)
                    : await InsertEventAsync(
                        connection,
                        transaction,
                        request.SiteId,
                        request.EdgeId,
                        effectiveEvent,
                        ct).ConfigureAwait(false);
                var analysisKey = effectiveEvent.ExecutionId ??
                                  $"{effectiveEvent.Subject.Type}:{effectiveEvent.Subject.Id}";
                analysisConfigurations.TryGetValue(analysisKey, out var analysis);
                var projectedSamples = await _timeSeriesStore.ProjectEventAsync(
                    connection,
                    transaction,
                    request.SiteId,
                    request.EdgeId,
                    ingestId,
                    ingestedAt,
                    effectiveEvent,
                    analysis,
                    ct).ConfigureAwait(false);
                if (isProcessSample && projectedSamples == 0)
                {
                    throw new InvalidDataException(
                        $"事件 {effectiveEvent.EventId} 没有可持久化的类型化过程值。");
                }
                await ProjectDataObjectSummaryAsync(
                    connection,
                    transaction,
                    request.SiteId,
                    request.EdgeId,
                    ingestId,
                    effectiveEvent,
                    ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(effectiveEvent.ExecutionId))
                {
                    acceptedMaxIngestByExecutionId[effectiveEvent.ExecutionId] = Math.Max(
                        acceptedMaxIngestByExecutionId.GetValueOrDefault(effectiveEvent.ExecutionId),
                        ingestId);
                }
                if (effectiveEvent.EventType.EndsWith(".started", StringComparison.Ordinal))
                {
                    await UpsertOperationContextSnapshotAsync(connection, transaction, effectiveEvent, ct)
                        .ConfigureAwait(false);
                }
                accepted++;
            }
            else
            {
                await VerifyDuplicateAsync(
                        connection, transaction, request.SiteId, request.EdgeId, evt, sourcePayloadHash, ct)
                    .ConfigureAwait(false);
                duplicates++;
            }
        }

        if (acceptedMaxIngestByExecutionId.Count > 0)
        {
            foreach (var pair in acceptedMaxIngestByExecutionId)
            {
                await _analysisMaterializations.MarkDirtyAsync(
                    connection,
                    transaction,
                    [pair.Key],
                    pair.Value,
                    "production_event_ingested",
                    ct).ConfigureAwait(false);
            }
            await EnqueueExecutionBoundaryProjectionAsync(
                    connection,
                    transaction,
                    request.SiteId,
                    request.EdgeId,
                    acceptedMaxIngestByExecutionId,
                    gapDetected,
                    ct)
                .ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(evt.ExecutionId))
            return evt;

        var executionId = evt.ExecutionId;
        var isStart = evt.EventType.EndsWith(".started", StringComparison.Ordinal);
        if (!isStart)
        {
            if (!capturedContexts.TryGetValue(executionId, out var captured))
            {
                captured = await LoadOperationContextSnapshotAsync(executionId, ct).ConfigureAwait(false);
                if (captured is not null)
                    capturedContexts[executionId] = captured;
            }
            return captured is null ? evt : evt with { Context = MergeCapturedContext(captured, evt.Context) };
        }

        var resolved = await _manufacturingContexts.ResolveAsync(evt.Subject.Id, evt.OccurredAt, ct)
            .ConfigureAwait(false);
        var context = new Dictionary<string, string>(evt.Context, StringComparer.Ordinal);
        context["execution_id"] = executionId;
        if (string.Equals(evt.Subject.Type, "equipment", StringComparison.OrdinalIgnoreCase))
            context["equipment_id"] = evt.Subject.Id;
        if (resolved is not null)
        {
            context["production_context_id"] = resolved.Production.ContextId.ToString("D");
            context["product_family_code"] = resolved.Production.ProductFamilyCode;
            context["product_code"] = resolved.Production.ProductCode;
            context["process_specification_id"] = resolved.Production.ProcessSpecificationId;
            context["process_specification_version"] = resolved.Production.ProcessSpecificationVersion;
            context["tooling_installation_id"] = resolved.Installation.InstallationId.ToString("D");
            context["tooling_assembly_id"] = resolved.Assembly.ToolingAssemblyId;
            context["tooling_assembly_id"] = resolved.Assembly.ToolingAssemblyId;
            context["assembly_revision_id"] = resolved.AssemblyRevision.AssemblyRevisionId.ToString("D");
            context["assembly_revision"] = resolved.AssemblyRevision.Revision.ToString(CultureInfo.InvariantCulture);
            context["context_captured_at"] = evt.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
            context["context_capture_status"] = "resolved";
            AddContext(context, "external_order_ref", resolved.Production.ExternalOrderRef);
            AddContext(context, "external_batch_ref", resolved.Production.ExternalBatchRef);
            AddContext(context, "material_lot_ref", resolved.Production.MaterialLotRef);
            AddContext(context, "material_specification", resolved.Production.MaterialSpecification);
            AddContext(context, "maintenance_status", resolved.Production.MaintenanceStatus);
            AddContext(context, "calibration_ref", resolved.Production.CalibrationRef);
            if (resolved.Production.CalibrationValidUntil is { } calibrationValidUntil)
            {
                context["calibration_valid_until"] = calibrationValidUntil
                    .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            var calibrationStatus = resolved.Production.CalibrationValidUntil is { } validUntil &&
                                    validUntil < evt.OccurredAt
                ? "expired"
                : resolved.Production.CalibrationStatus ??
                  (resolved.Production.CalibrationValidUntil.HasValue ? "valid" : null);
            AddContext(context, "calibration_status", calibrationStatus);
        }
        else
        {
            context["context_captured_at"] = evt.OccurredAt.ToString("O", CultureInfo.InvariantCulture);
            // Imports may already carry immutable product, process-specification, and output-item
            // context from their source system.  Do not turn a usable historical
            // replay into a configuration error merely because it did not pass
            // through this deployment's live preparation and tooling workflow.
            // This remains distinct from a locally resolved live context.
            context["context_capture_status"] = HasSourceProvidedContext(context)
                ? "source_provided"
                : "configuration_missing";
        }

        var processSpecification = await _analysisResolver.ResolveProcessSpecificationAsync(context, ct).ConfigureAwait(false);
        var analysisScope = evt.EventType.StartsWith("run.", StringComparison.Ordinal)
            ? "production-run"
            : "production-execution";
        var analysis = await _analysisResolver.ResolveAsync(context, analysisScope, ct).ConfigureAwait(false);
        if (processSpecification is not null)
        {
            var sourceModelId = ProcessAnalysisResolver.ContextValue(context, "data_model_id")?.Trim();
            var hasSourceModelVersion = int.TryParse(
                ProcessAnalysisResolver.ContextValue(context, "data_model_version"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var sourceModelVersion) && sourceModelVersion > 0;
            var hasSourceModel = !string.IsNullOrWhiteSpace(sourceModelId) && hasSourceModelVersion;

            // 采集配置声明的是本次原始轨迹实际采用的数据模型，不能被工艺规范主数据的
            // 历史模型版本覆盖。工艺规范模型单独留痕；不一致时显式标记，交给数据质量
            // 和配置治理处理，但仍按采集模型校验并保存真实设备数据。
            context["process_specification_data_model_id"] = processSpecification.DataModelId;
            context["process_specification_data_model_version"] = processSpecification.DataModelVersion.ToString(CultureInfo.InvariantCulture);
            if (!hasSourceModel)
            {
                context["data_model_id"] = processSpecification.DataModelId;
                context["data_model_version"] = processSpecification.DataModelVersion.ToString(CultureInfo.InvariantCulture);
                context["process_specification_snapshot_status"] = "resolved";
            }
            else
            {
                context["process_specification_snapshot_status"] =
                    string.Equals(sourceModelId, processSpecification.DataModelId, StringComparison.Ordinal) &&
                    sourceModelVersion == processSpecification.DataModelVersion
                        ? "resolved"
                        : "model_mismatch";
            }
        }
        if (analysis is not null)
        {
            context["analysis_plan_id"] = analysis.Plan.PlanId;
            context["analysis_plan_version"] = analysis.Plan.Version.ToString(CultureInfo.InvariantCulture);
        }

        var data = new Dictionary<string, object?>(evt.Data, StringComparer.Ordinal);
        if (processSpecification is not null)
        {
            data["plannedControlParameterValues"] = processSpecification.Values.ToDictionary(
                static item => item.Code,
                static item => (object?)item.Value,
                StringComparer.Ordinal);
        }
        capturedContexts[executionId] = context;
        return evt with { Context = context, Data = data };
    }

    private async Task<IReadOnlyDictionary<string, string>?> LoadOperationContextSnapshotAsync(
        string executionId,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT context::text FROM operation_context_snapshots WHERE execution_id = @execution_id;");
        command.Parameters.AddWithValue("execution_id", executionId);
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
              execution_id, subject_type, subject_id, started_event_type, captured_at, context)
            VALUES (@execution_id, @subject_type, @subject_id, @started_event_type, @captured_at, @context)
            ON CONFLICT (execution_id) DO UPDATE SET
              subject_type = EXCLUDED.subject_type,
              subject_id = EXCLUDED.subject_id,
              started_event_type = EXCLUDED.started_event_type,
              captured_at = EXCLUDED.captured_at,
              context = EXCLUDED.context;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("execution_id", evt.ExecutionId!);
        command.Parameters.AddWithValue("subject_type", evt.Subject.Type);
        command.Parameters.AddWithValue("subject_id", evt.Subject.Id);
        command.Parameters.AddWithValue("started_event_type", evt.EventType);
        command.Parameters.AddWithValue("captured_at", evt.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("context", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Context, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<ProductionEvent> AttachToolingUsageOrdinalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProductionEvent evt,
        CancellationToken ct)
    {
        if (!evt.Context.TryGetValue("tooling_installation_id", out var rawInstallationId) ||
            !Guid.TryParse(rawInstallationId, out var installationId))
        {
            return evt;
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO tooling_usage_counters(tooling_installation_id, started_run_count, updated_at)
            VALUES (@installation_id, 1, now())
            ON CONFLICT (tooling_installation_id) DO UPDATE SET
              started_run_count = tooling_usage_counters.started_run_count + 1,
              updated_at = EXCLUDED.updated_at
            RETURNING started_run_count;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("installation_id", installationId);
        var ordinal = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        var context = new Dictionary<string, string>(evt.Context, StringComparer.Ordinal)
        {
            ["tooling_usage_count"] = ordinal.ToString(CultureInfo.InvariantCulture),
            ["tooling_usage_unit"] = "started_runs"
        };
        return evt with { Context = context };
    }

    private static IReadOnlyDictionary<string, string> MergeCapturedContext(
        IReadOnlyDictionary<string, string> captured,
        IReadOnlyDictionary<string, string> current)
    {
        var result = new Dictionary<string, string>(captured, StringComparer.Ordinal);
        foreach (var pair in current)
        {
            if (pair.Key is "stage_number" or "process_stage_name" or
                "process_step" or "process_step_name" or "process_phase" or "process_stage" ||
                !result.ContainsKey(pair.Key))
                result[pair.Key] = pair.Value;
        }
        return result;
    }

    private static bool HasSourceProvidedContext(IReadOnlyDictionary<string, string> context)
        => new[] { "product_family_code", "product_code", "process_specification_id", "process_specification_version", "output_item_id" }
            .All(key => context.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value));

    private async Task ValidateProcessSampleAsync(
        ProductionEvent evt,
        IDictionary<string, ResolvedProcessAnalysis?> configurations,
        CancellationToken ct)
    {
        if (!string.Equals(evt.EventType, "process.sample", StringComparison.Ordinal))
            return;
        var cacheKey = evt.ExecutionId ?? $"{evt.Subject.Type}:{evt.Subject.Id}";
        if (!configurations.TryGetValue(cacheKey, out var analysis))
        {
            analysis = await _analysisResolver.ResolveAsync(evt.Context, "production-execution", ct).ConfigureAwait(false)
                       ?? await _analysisResolver.ResolveAsync(evt.Context, "production-run", ct).ConfigureAwait(false)
                       ?? await _analysisResolver.ResolveAsync(evt.Context, "analysis-window", ct).ConfigureAwait(false);
            configurations[cacheKey] = analysis;
        }
        if (analysis is null)
            throw new ArgumentException(
                $"事件 {evt.EventId} 无法解析已发布的工艺数据模型，不能持久化过程采样。");
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
                              SELECT ingest_id, site_id, edge_id, ingested_at, event_id, schema_version, event_type, type_version,
                                     occurred_at, recorded_at, source, subject_type, subject_id,
                                     execution_id, configuration_kind, configuration_id, configuration_version,
                                     quality_flags::text, payload_hash, context::text, data::text, seq
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

    public async Task<IReadOnlyList<PlatformProductionEvent>> QueryByExecutionIdsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executionIds);
        await InitializeAsync(ct).ConfigureAwait(false);
        var ids = executionIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return [];

        await using var command = _dataSource.CreateCommand(
            """
            SELECT ingest_id, site_id, edge_id, ingested_at, event_id, schema_version, event_type, type_version,
                   occurred_at, recorded_at, source, subject_type, subject_id,
                   execution_id, configuration_kind, configuration_id, configuration_version,
                   quality_flags::text, payload_hash, context::text, data::text, seq
            FROM production_events
            WHERE execution_id = ANY(@execution_ids)
            ORDER BY execution_id, occurred_at, ingest_id;
            """);
        command.Parameters.AddWithValue("execution_ids", ids);
        var result = new List<PlatformProductionEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadEvent(reader));
        return result;
    }

    public async Task<IReadOnlyList<PlatformProcessExecutionSummarySource>> QueryExecutionSummarySourcesAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executionIds);
        await InitializeAsync(ct).ConfigureAwait(false);
        var ids = executionIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return [];

        await using var command = _dataSource.CreateCommand(
            """
            WITH sample_counts AS (
              SELECT execution_id, count(*) AS sample_count
              FROM process_sample_frames
              WHERE execution_id = ANY(@execution_ids)
              GROUP BY execution_id
            )
            SELECT event.ingest_id, event.site_id, event.edge_id, event.ingested_at,
                   event.event_id, event.event_type, event.type_version,
                   event.occurred_at, event.recorded_at, event.source,
                   event.subject_type, event.subject_id, event.execution_id,
                   event.context::text, event.data::text, event.seq,
                   COALESCE(sample_counts.sample_count, 0)
            FROM production_events AS event
            LEFT JOIN sample_counts USING (execution_id)
            WHERE event.execution_id = ANY(@execution_ids)
            ORDER BY event.execution_id, event.occurred_at, event.ingest_id;
            """);
        command.Parameters.AddWithValue("execution_ids", ids);
        var eventsByExecution = new Dictionary<string, List<PlatformProductionEvent>>(StringComparer.Ordinal);
        var sampleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var row = ReadEvent(reader);
            var executionId = row.Event.ExecutionId!;
            if (!eventsByExecution.TryGetValue(executionId, out var rows))
            {
                rows = [];
                eventsByExecution[executionId] = rows;
            }
            rows.Add(row);
            sampleCounts[executionId] = checked((int)reader.GetInt64(16));
        }
        return ids
            .Where(eventsByExecution.ContainsKey)
            .Select(id => new PlatformProcessExecutionSummarySource
            {
                ExecutionId = id,
                SampleCount = sampleCounts.GetValueOrDefault(id),
                Events = eventsByExecution[id]
            })
            .ToArray();
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
        AddEquality(command, predicates, "site_id", "site_id", query.SiteId);
        AddEquality(command, predicates, "edge_id", "edge_id", query.EdgeId);
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
        command.CommandText = string.IsNullOrWhiteSpace(query.EdgeId) && !query.From.HasValue && !query.To.HasValue
            ? $"""
               SELECT site_id, subject_type, subject_id, edge_id, event_count, sample_count, operation_count,
                      first_observed_at, last_observed_at, last_sample_at, maximum_sample_gap_seconds,
                      latest_event_type, context::text, count(*) OVER() AS total_count
               FROM data_object_summaries
               {where}
               ORDER BY last_observed_at DESC, subject_type, subject_id
               LIMIT @limit OFFSET @offset;
               """
            : $"""
                              WITH sample_frames AS (
                                SELECT event_id, frame_id AS ingest_id, site_id, edge_id,
                                       'process.sample'::text AS event_type, occurred_at,
                                       frame.subject_type, frame.subject_id, frame.execution_id,
                                       coalesce(snapshot.context, jsonb_build_object()) AS context
                                FROM process_sample_frames AS frame
                                LEFT JOIN operation_context_snapshots AS snapshot
                                  ON snapshot.execution_id = frame.execution_id
                              ),
                              records AS (
                                SELECT ingest_id, site_id, edge_id, event_type, occurred_at, subject_type,
                                       subject_id, execution_id, context
                                FROM production_events
                                UNION ALL
                                SELECT ingest_id, site_id, edge_id, event_type, occurred_at, subject_type,
                                       subject_id, execution_id, context
                                FROM sample_frames
                              ),
                              filtered AS (
                                SELECT * FROM records
                                {where}
                              ),
                              aggregate_rows AS (
                                SELECT site_id, subject_type, subject_id,
                                       count(*) AS event_count,
                                       count(*) FILTER (WHERE event_type = 'process.sample') AS sample_count,
                                       count(DISTINCT execution_id) AS operation_count,
                                       min(occurred_at) AS first_observed_at,
                                       max(occurred_at) AS last_observed_at,
                                       max(occurred_at) FILTER (WHERE event_type = 'process.sample') AS last_sample_at
                                FROM filtered
                                GROUP BY site_id, subject_type, subject_id
                              ),
                              latest_rows AS (
                                SELECT DISTINCT ON (site_id, subject_type, subject_id)
                                       site_id, subject_type, subject_id, edge_id, event_type, context
                                FROM filtered
                                ORDER BY site_id, subject_type, subject_id, occurred_at DESC, ingest_id DESC
                              ),
                              sample_intervals AS (
                                SELECT site_id, subject_type, subject_id,
                                       EXTRACT(EPOCH FROM occurred_at - lag(occurred_at) OVER (
                                         PARTITION BY site_id, subject_type, subject_id ORDER BY occurred_at, ingest_id
                                       )) AS gap_seconds
                                FROM filtered
                                WHERE event_type = 'process.sample'
                              ),
                              gap_rows AS (
                                SELECT site_id, subject_type, subject_id, max(gap_seconds) AS maximum_sample_gap_seconds
                                FROM sample_intervals
                                GROUP BY site_id, subject_type, subject_id
                              )
                              SELECT aggregate_rows.site_id, aggregate_rows.subject_type, aggregate_rows.subject_id,
                                     latest_rows.edge_id, aggregate_rows.event_count,
                                     aggregate_rows.sample_count, aggregate_rows.operation_count,
                                     aggregate_rows.first_observed_at, aggregate_rows.last_observed_at,
                                     aggregate_rows.last_sample_at, gap_rows.maximum_sample_gap_seconds,
                                     latest_rows.event_type, latest_rows.context::text,
                                     count(*) OVER() AS total_count
                              FROM aggregate_rows
                              JOIN latest_rows USING (site_id, subject_type, subject_id)
                              LEFT JOIN gap_rows USING (site_id, subject_type, subject_id)
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
            total = checked((int)reader.GetInt64(13));
            rows.Add(new DataObjectSummary
            {
                SiteId = reader.GetString(0),
                SubjectType = reader.GetString(1),
                SubjectId = reader.GetString(2),
                EdgeId = reader.IsDBNull(3) ? null : reader.GetString(3),
                EventCount = reader.GetInt64(4),
                SampleCount = reader.GetInt64(5),
                OperationCount = reader.GetInt64(6),
                FirstObservedAt = ReadTimestamp(reader, 7),
                LastObservedAt = ReadTimestamp(reader, 8),
                LastSampleAt = ReadTimestamp(reader, 9),
                MaximumSampleGapSeconds = reader.IsDBNull(10)
                    ? null
                    : Convert.ToDouble(reader.GetValue(10), CultureInfo.InvariantCulture),
                LatestEventType = reader.IsDBNull(11) ? null : reader.GetString(11),
                Context = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(12), JsonOptions)
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
        AddEquality(command, predicates, "site_id", "site_id", query.SiteId);
        AddEquality(command, predicates, "edge_id", "edge_id", query.EdgeId);
        AddEquality(command, predicates, "event_type", "event_type", query.EventType);
        AddEquality(command, predicates, "subject_type", "subject_type", query.SubjectType);
        AddEquality(command, predicates, "subject_id", "subject_id", query.SubjectId);
        AddEquality(command, predicates, "execution_id", "execution_id", query.ExecutionId);
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var search = query.SearchText.Trim();
            if (search.Length > 128)
                throw new ArgumentException("运行搜索词不能超过 128 个字符。", nameof(query));
            predicates.Add("""
                           (execution_id ILIKE @search ESCAPE '~'
                            OR subject_id ILIKE @search ESCAPE '~'
                            OR context ->> 'product_family_code' ILIKE @search ESCAPE '~'
                            OR context ->> 'product_code' ILIKE @search ESCAPE '~'
                            OR context ->> 'process_specification_id' ILIKE @search ESCAPE '~')
                           """);
            command.Parameters.AddWithValue("search", $"%{EscapeLike(search)}%");
        }

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

    private static string EscapeLike(string value)
        => value.Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal);

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

    public void Dispose() => _initializeLock.Dispose();

    private static async Task<long?> GetMaxEdgeSeqAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT MAX(seq) FROM event_ingest_keys WHERE site_id = @site_id AND edge_id = @edge_id;",
            connection,
            transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TryReserveEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        ProductionEvent evt,
        string sourcePayloadHash,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO event_ingest_keys(event_id, site_id, edge_id, seq, occurred_at, payload_hash)
            VALUES (@event_id, @site_id, @edge_id, @seq, @occurred_at, @payload_hash)
            ON CONFLICT DO NOTHING
            RETURNING event_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_id", evt.EventId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("seq", evt.Seq);
        command.Parameters.AddWithValue("occurred_at", evt.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("payload_hash", sourcePayloadHash);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task EnqueueExecutionBoundaryProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        IReadOnlyDictionary<string, long> maximumIngestIds,
        bool gapDetected,
        CancellationToken ct)
    {
        foreach (var pair in maximumIngestIds)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO execution_boundary_recompute_jobs(
                  site_id, source_execution_id, edge_id, requested_max_ingest_id,
                  gap_detected, status, available_at, updated_at)
                VALUES (
                  @site_id, @execution_id, @edge_id, @maximum_ingest_id,
                  @gap_detected, 'queued', now(), now())
                ON CONFLICT (site_id, source_execution_id) DO UPDATE SET
                  edge_id = EXCLUDED.edge_id,
                  requested_max_ingest_id = GREATEST(
                    execution_boundary_recompute_jobs.requested_max_ingest_id,
                    EXCLUDED.requested_max_ingest_id),
                  gap_detected = execution_boundary_recompute_jobs.gap_detected OR EXCLUDED.gap_detected,
                  status = CASE
                    WHEN execution_boundary_recompute_jobs.status = 'running' THEN 'running'
                    ELSE 'queued'
                  END,
                  available_at = CASE
                    WHEN execution_boundary_recompute_jobs.status = 'running'
                      THEN execution_boundary_recompute_jobs.available_at
                    ELSE now()
                  END,
                  last_error = NULL,
                  updated_at = now();
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("site_id", siteId);
            command.Parameters.AddWithValue("execution_id", pair.Key);
            command.Parameters.AddWithValue("edge_id", edgeId);
            command.Parameters.AddWithValue("maximum_ingest_id", pair.Value);
            command.Parameters.AddWithValue("gap_detected", gapDetected);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static async Task<long> InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        ProductionEvent evt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO production_events(
              event_id, site_id, edge_id, seq, schema_version, event_type, type_version, occurred_at, recorded_at,
              source, subject_type, subject_id, execution_id,
              configuration_kind, configuration_id, configuration_version,
              quality_flags, payload_hash, context, data)
            VALUES (
              @event_id, @site_id, @edge_id, @seq, @schema_version, @event_type, @type_version, @occurred_at, @recorded_at,
              @source, @subject_type, @subject_id, @execution_id,
              @configuration_kind, @configuration_id, @configuration_version,
              @quality_flags, @payload_hash, @context, @data)
            RETURNING ingest_id;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_id", evt.EventId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("seq", evt.Seq);
        command.Parameters.AddWithValue("schema_version", evt.SchemaVersion);
        command.Parameters.AddWithValue("event_type", evt.EventType);
        command.Parameters.AddWithValue("type_version", evt.EventTypeVersion);
        command.Parameters.AddWithValue("occurred_at", evt.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("recorded_at", evt.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("source", evt.Source);
        command.Parameters.AddWithValue("subject_type", evt.Subject.Type);
        command.Parameters.AddWithValue("subject_id", evt.Subject.Id);
        command.Parameters.AddWithValue("execution_id", (object?)evt.ExecutionId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "configuration_kind",
            (object?)evt.AppliedConfiguration?.Kind ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "configuration_id",
            (object?)evt.AppliedConfiguration?.Id ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "configuration_version",
            (object?)evt.AppliedConfiguration?.Version ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "quality_flags",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(evt.QualityFlags, JsonOptions));
        command.Parameters.AddWithValue("payload_hash", evt.PayloadHash);
        command.Parameters.AddWithValue("context", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Context, JsonOptions));
        command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Data, JsonOptions));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<long> AllocateIngestIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT nextval('production_events_ingest_id_seq');",
            connection,
            transaction);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ProjectDataObjectSummaryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        long ingestId,
        ProductionEvent evt,
        CancellationToken ct)
    {
        var operationIncrement = 0;
        if (!string.IsNullOrWhiteSpace(evt.ExecutionId))
        {
            await using var operationCommand = new NpgsqlCommand(
                """
                INSERT INTO data_object_operation_keys(site_id, subject_type, subject_id, execution_id)
                VALUES (@site_id, @subject_type, @subject_id, @execution_id)
                ON CONFLICT DO NOTHING
                RETURNING execution_id;
                """,
                connection,
                transaction);
            operationCommand.Parameters.AddWithValue("site_id", siteId);
            operationCommand.Parameters.AddWithValue("subject_type", evt.Subject.Type);
            operationCommand.Parameters.AddWithValue("subject_id", evt.Subject.Id);
            operationCommand.Parameters.AddWithValue("execution_id", evt.ExecutionId);
            operationIncrement = await operationCommand.ExecuteScalarAsync(ct).ConfigureAwait(false) is null ? 0 : 1;
        }

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO data_object_summaries(
              site_id, subject_type, subject_id, edge_id, event_count, sample_count, operation_count,
              first_observed_at, last_observed_at, last_sample_at, maximum_sample_gap_seconds,
              latest_event_type, context, latest_ingest_id)
            VALUES (
              @site_id, @subject_type, @subject_id, @edge_id, 1, @sample_count, @operation_count,
              @occurred_at, @occurred_at, @last_sample_at, NULL,
              @event_type, @context, @ingest_id)
            ON CONFLICT (site_id, subject_type, subject_id) DO UPDATE SET
              event_count = data_object_summaries.event_count + 1,
              sample_count = data_object_summaries.sample_count + EXCLUDED.sample_count,
              operation_count = data_object_summaries.operation_count + EXCLUDED.operation_count,
              first_observed_at = LEAST(data_object_summaries.first_observed_at, EXCLUDED.first_observed_at),
              last_observed_at = GREATEST(data_object_summaries.last_observed_at, EXCLUDED.last_observed_at),
              maximum_sample_gap_seconds = CASE
                WHEN EXCLUDED.last_sample_at IS NULL THEN data_object_summaries.maximum_sample_gap_seconds
                WHEN data_object_summaries.last_sample_at IS NULL THEN data_object_summaries.maximum_sample_gap_seconds
                WHEN EXCLUDED.last_sample_at >= data_object_summaries.last_sample_at THEN GREATEST(
                  COALESCE(data_object_summaries.maximum_sample_gap_seconds, 0),
                  EXTRACT(EPOCH FROM EXCLUDED.last_sample_at - data_object_summaries.last_sample_at))
                ELSE data_object_summaries.maximum_sample_gap_seconds
              END,
              last_sample_at = CASE
                WHEN EXCLUDED.last_sample_at IS NULL THEN data_object_summaries.last_sample_at
                ELSE GREATEST(data_object_summaries.last_sample_at, EXCLUDED.last_sample_at)
              END,
              edge_id = CASE
                WHEN (EXCLUDED.last_observed_at, EXCLUDED.latest_ingest_id) >=
                     (data_object_summaries.last_observed_at, data_object_summaries.latest_ingest_id)
                THEN EXCLUDED.edge_id ELSE data_object_summaries.edge_id
              END,
              latest_event_type = CASE
                WHEN (EXCLUDED.last_observed_at, EXCLUDED.latest_ingest_id) >=
                     (data_object_summaries.last_observed_at, data_object_summaries.latest_ingest_id)
                THEN EXCLUDED.latest_event_type ELSE data_object_summaries.latest_event_type
              END,
              context = CASE
                WHEN (EXCLUDED.last_observed_at, EXCLUDED.latest_ingest_id) >=
                     (data_object_summaries.last_observed_at, data_object_summaries.latest_ingest_id)
                THEN EXCLUDED.context ELSE data_object_summaries.context
              END,
              latest_ingest_id = GREATEST(data_object_summaries.latest_ingest_id, EXCLUDED.latest_ingest_id);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("subject_type", evt.Subject.Type);
        command.Parameters.AddWithValue("subject_id", evt.Subject.Id);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("sample_count", evt.EventType == "process.sample" ? 1 : 0);
        command.Parameters.AddWithValue("operation_count", operationIncrement);
        command.Parameters.AddWithValue("occurred_at", evt.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "last_sample_at",
            evt.EventType == "process.sample" ? evt.OccurredAt.UtcDateTime : DBNull.Value);
        command.Parameters.AddWithValue("event_type", evt.EventType);
        command.Parameters.AddWithValue("context", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(evt.Context, JsonOptions));
        command.Parameters.AddWithValue("ingest_id", ingestId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task VerifyDuplicateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        ProductionEvent evt,
        string sourcePayloadHash,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT event_id, site_id, edge_id, seq, payload_hash
            FROM event_ingest_keys
            WHERE event_id = @event_id OR
                  (site_id = @site_id AND edge_id = @edge_id AND seq = @seq)
            LIMIT 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_id", evt.EventId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("seq", evt.Seq);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) ||
            !string.Equals(reader.GetString(0), evt.EventId, StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), siteId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(reader.GetString(2), edgeId, StringComparison.OrdinalIgnoreCase) ||
            reader.GetInt64(3) != evt.Seq ||
            !string.Equals(reader.GetString(4), sourcePayloadHash, StringComparison.Ordinal))
        {
            throw new EventIngestConflictException(
                $"事件幂等键或载荷冲突：SiteId={siteId}, EdgeId={edgeId}, Seq={evt.Seq}, EventId={evt.EventId}");
        }
    }

    private static PlatformProductionEvent ReadEvent(NpgsqlDataReader reader)
    {
        var context = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(19), JsonOptions)
                      ?? new Dictionary<string, string>();
        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(reader.GetString(20), JsonOptions)
                   ?? new Dictionary<string, object?>();
        var evt = new ProductionEvent
        {
            EventId = reader.GetString(4),
            SchemaVersion = reader.GetInt32(5),
            EventType = reader.GetString(6),
            EventTypeVersion = reader.GetInt32(7),
            OccurredAt = new DateTimeOffset(reader.GetDateTime(8).ToUniversalTime()),
            RecordedAt = new DateTimeOffset(reader.GetDateTime(9).ToUniversalTime()),
            Source = reader.GetString(10),
            Subject = new ObjectRef(reader.GetString(11), reader.GetString(12)),
            ExecutionId = reader.IsDBNull(13) ? null : reader.GetString(13),
            AppliedConfiguration = reader.IsDBNull(14)
                ? null
                : new AppliedConfigurationRef(
                    reader.GetString(14),
                    reader.GetString(15),
                    reader.GetInt32(16)),
            QualityFlags = JsonSerializer.Deserialize<string[]>(reader.GetString(17), JsonOptions) ?? [],
            PayloadHash = reader.GetString(18),
            Context = context,
            Data = data,
            Seq = reader.GetInt64(21)
        };
        return new PlatformProductionEvent
        {
            IngestId = reader.GetInt64(0),
            SiteId = reader.GetString(1),
            EdgeId = reader.GetString(2),
            IngestedAt = new DateTimeOffset(reader.GetDateTime(3).ToUniversalTime()),
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
