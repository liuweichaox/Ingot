// 从有限范围的生产事件和检验事实计算可审计的数据可靠性基线。

using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessExecutions;

namespace Ingot.Platform.Infrastructure.Analytics;

public sealed class DataReliabilityBaselineService(
    IPlatformEventStore events,
    IExecutionComparisonService executions,
    ResearchContextAdmissionEvaluator? contextAdmission = null) : IDataReliabilityBaselineService
{
    private readonly ResearchContextAdmissionEvaluator _contextAdmission =
        contextAdmission ?? new ResearchContextAdmissionEvaluator();

    private static readonly string[] ContextFieldsToTrack =
    [
        "context_capture_status",
        "control_parameter_capture_status",
        "equipment_id",
        "execution_id",
        "product_family_code",
        "product_code",
        "process_specification_id",
        "process_specification_version",
        "output_item_id",
        "production_context_id",
        "material_lot_ref",
        "material_specification",
        "external_order_ref",
        "external_batch_ref",
        "tooling_installation_id",
        "tooling_assembly_id",
        "assembly_revision",
        "assembly_revision_id",
        "tooling_usage_count",
        "maintenance_status",
        "calibration_status",
        "calibration_ref",
        "calibration_valid_until"
    ];

    private static readonly string[] RequiredContextFields = ["equipment_id", "execution_id"];

    private static readonly (string Field, string Name)[] ContextFactors =
    [
        ("equipment_id", "设备"),
        ("tooling_assembly_id", "工装"),
        ("material_lot_ref", "材料批次")
    ];

    private static readonly (string Code, string Name)[] AdmissionExclusionReasons =
    [
        ("lifecycle_incomplete", "运行生命周期不完整"),
        ("process_data_incomplete", "过程数据未达到完整标准"),
        ("actual_parameters_missing", "缺少设备实际参数回读"),
        ("parameter_unit_missing", "实际参数或单位不完整"),
        ("context_capture_invalid", "生产上下文捕获失败"),
        ("minimal_context_missing", "最小上下文快照不完整"),
        ("quality_unlinked", "没有关联有效质量结果")
    ];

    private const int MaximumFactorLevels = 50;
    private const int MaximumScannedEvents = 50_000;

    public async Task<DataReliabilityBaseline> CalculateAsync(
        DataReliabilityBaselineQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SiteId))
            throw new ArgumentException("数据可靠性基线必须指定站点。", nameof(query));
        if (query.From > query.To)
            throw new ArgumentException("开始时间不能晚于结束时间。", nameof(query));
        var generatedAt = DateTimeOffset.UtcNow;
        var maximumRuns = Math.Clamp(query.MaximumRuns, 1, 2000);
        var scopedQuery = new PlatformEventQuery
        {
            SiteId = Normalize(query.SiteId),
            EdgeId = Normalize(query.EdgeId),
            EquipmentId = Normalize(query.EquipmentId),
            SubjectType = Normalize(query.SubjectType)?.ToLowerInvariant(),
            SubjectId = Normalize(query.SubjectId),
            From = query.From,
            To = query.To
        };
        var lifecycleEvents = await Task.WhenAll(
                QueryAllAsync(scopedQuery with { EventType = "process.execution.started" }, ct),
                QueryAllAsync(scopedQuery with { EventType = "process.execution.completed" }, ct))
            .ConfigureAwait(false);
        var completedEvents = lifecycleEvents[1];
        var startedPlatformIngestedAtByExecution = LifecycleReceiptTimesByExecution(
            lifecycleEvents[0], earliest: true);
        var completedPlatformIngestedAtByExecution = LifecycleReceiptTimesByExecution(
            completedEvents, earliest: false);
        var matchingIds = lifecycleEvents
            .SelectMany(static items => items)
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId))
            .GroupBy(static item => item.Event.ExecutionId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Select(static item => item.Event.ExecutionId!)
            .ToArray();
        var completedIds = completedEvents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId))
            .Select(static item => item.Event.ExecutionId!)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var selectedIds = matchingIds.Take(maximumRuns).ToArray();
        var executionMap = await executions.GetProcessExecutionsAsync(
            selectedIds, ct, Normalize(query.SiteId)).ConfigureAwait(false);
        var rows = selectedIds
            .Where(executionMap.ContainsKey)
            .Select(id => executionMap[id])
            .ToArray();
        var evaluatedRows = rows
            .Select(row => (Row: row, ReasonCodes: AdmissionExclusionCodes(row)))
            .ToArray();
        var impactedRows = evaluatedRows
            .Where(static item => item.ReasonCodes.Count > 0)
            .ToArray();
        var rowsNeedingLifecycleBoundaries = impactedRows.Select(static item => item.Row).ToArray();
        var scopedLifecycleBoundariesMayBePartial = query.From.HasValue || query.To.HasValue ||
            !string.IsNullOrWhiteSpace(query.EdgeId) || !string.IsNullOrWhiteSpace(query.EquipmentId) ||
            !string.IsNullOrWhiteSpace(query.SubjectType) || !string.IsNullOrWhiteSpace(query.SubjectId);
        var startedBoundaryRecoveryIds = scopedLifecycleBoundariesMayBePartial
            ? rowsNeedingLifecycleBoundaries
                .Where(static row => row.HasStarted)
                .Select(static row => row.ExecutionId)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];
        var completedBoundaryRecoveryIds = scopedLifecycleBoundariesMayBePartial
            ? rowsNeedingLifecycleBoundaries
                .Where(static row => row.HasCompleted)
                .Select(static row => row.ExecutionId)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (startedBoundaryRecoveryIds.Length > 0 || completedBoundaryRecoveryIds.Length > 0)
        {
            // Re-read full lifecycle boundaries for scoped runs without loading sample-event types.
            var boundaryQuery = new PlatformEventQuery
            {
                SiteId = scopedQuery.SiteId,
                Limit = 500
            };
            IReadOnlyList<PlatformProductionEvent> empty = [];
            var recoveredBoundaries = await Task.WhenAll(
                    startedBoundaryRecoveryIds.Length == 0
                        ? Task.FromResult(empty)
                        : QueryAllAsync(boundaryQuery with
                        {
                            ExecutionIds = startedBoundaryRecoveryIds,
                            EventType = "process.execution.started"
                        }, ct),
                    completedBoundaryRecoveryIds.Length == 0
                        ? Task.FromResult(empty)
                        : QueryAllAsync(boundaryQuery with
                        {
                            ExecutionIds = completedBoundaryRecoveryIds,
                            EventType = "process.execution.completed"
                        }, ct))
                .ConfigureAwait(false);
            foreach (var pair in LifecycleReceiptTimesByExecution(recoveredBoundaries[0], earliest: true))
                startedPlatformIngestedAtByExecution[pair.Key] = pair.Value;
            foreach (var pair in LifecycleReceiptTimesByExecution(recoveredBoundaries[1], earliest: false))
                completedPlatformIngestedAtByExecution[pair.Key] = pair.Value;
        }
        var runImpacts = new List<ReliabilityRunImpact>(impactedRows.Length);
        foreach (var item in impactedRows)
        {
            runImpacts.Add(new ReliabilityRunImpact
            {
                ExecutionId = item.Row.ExecutionId,
                EquipmentId = item.Row.EquipmentId,
                SubjectType = item.Row.SubjectType,
                SubjectId = item.Row.SubjectId,
                EdgeIds = item.Row.EdgeIds,
                HasStarted = item.Row.HasStarted,
                HasCompleted = item.Row.HasCompleted,
                StartedAt = item.Row.HasStarted ? item.Row.StartedAt : null,
                CompletedAt = item.Row.HasCompleted ? item.Row.CompletedAt : null,
                StartedPlatformIngestedAt = startedPlatformIngestedAtByExecution.GetValueOrDefault(
                    item.Row.ExecutionId),
                CompletedPlatformIngestedAt = completedPlatformIngestedAtByExecution.GetValueOrDefault(
                    item.Row.ExecutionId),
                ProcessDataStatus = item.Row.ProcessDataQuality.Status,
                ReasonCodes = item.ReasonCodes,
                SampleConfigurationEvidence = item.Row.SampleConfigurationEvidence
            });
        }

        var denominator = rows.Length;
        var lifecycleComplete = rows.Count(static row => row.LifecycleComplete);
        var processComplete = rows.Count(static row =>
            row.ProcessDataQuality.Status == ProcessDataStatuses.Available);
        var processUsable = rows.Count(static row =>
            row.ProcessDataQuality.Status != ProcessDataStatuses.Unavailable);
        var actualParameters = rows.Count(HasActualParameters);
        var parameterUnits = rows.Count(HasCompleteActualParameterUnits);
        var contextIntegrity = rows.Count(row =>
            _contextAdmission.Evaluate(row.Context, null).Admitted);
        var minimalContext = rows.Count(HasMinimalContext);
        var qualityLinked = rows.Count(static row => row.InspectionOutcomes.Count > 0);
        var eligible = evaluatedRows.Count(static item => item.ReasonCodes.Count == 0);

        var reasonCounts = evaluatedRows
            .SelectMany(static item => item.ReasonCodes)
            .GroupBy(static code => code, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var exclusions = AdmissionExclusionReasons
            .Select(reason => Exclusion(
                reason.Code,
                reason.Name,
                reasonCounts.GetValueOrDefault(reason.Code)))
            .Where(static item => item.RunCount > 0)
            .ToArray();

        var factorSummaries = BuildFactorSummaries(rows);
        var factorOverlaps = BuildFactorOverlaps(rows);
        return new DataReliabilityBaseline
        {
            GeneratedAt = generatedAt,
            SiteId = Normalize(query.SiteId),
            From = query.From,
            To = query.To,
            EdgeId = Normalize(query.EdgeId),
            EquipmentId = Normalize(query.EquipmentId),
            SubjectType = Normalize(query.SubjectType)?.ToLowerInvariant(),
            SubjectId = Normalize(query.SubjectId),
            MatchingCompletedRunCount = completedIds.Count,
            MatchingObservedRunCount = matchingIds.Length,
            AnalyzedRunCount = denominator,
            MissingExecutionRowCount = selectedIds.Length - rows.Length,
            Truncated = matchingIds.Length > maximumRuns,
            Rates =
            [
                Rate("lifecycle_completeness", "运行生命周期完整率", lifecycleComplete, denominator,
                    "同时存在 process.execution.started 与 process.execution.completed 的运行 / 已分析运行。"),
                Rate("process_data_completeness", "过程数据完整率", processComplete, denominator,
                    "过程数据状态为“可用”的运行 / 已分析运行；“降级”状态不计为完整。"),
                Rate("process_data_usability", "过程数据可用率", processUsable, denominator,
                    "过程数据状态不是“不可用”的运行 / 已分析运行。"),
                Rate("actual_parameter_coverage", "实际参数覆盖率", actualParameters, denominator,
                    "存在“配方版本已应用”现场实际参数回读的运行 / 已分析运行；不使用配方版本计划值。"),
                Rate("actual_parameter_unit_completeness", "实际参数单位完整率", parameterUnits, denominator,
                    "全部实际参数具有明确单位的运行 / 已分析运行。"),
                Rate("context_capture_integrity", "生产上下文捕获可信率", contextIntegrity, denominator,
                    "上下文未标记为“配置缺失”或其他无效捕获状态的运行 / 已分析运行。"),
                Rate("minimal_context_coverage", "最小上下文覆盖率", minimalContext, denominator,
                    "上下文捕获有效且同时包含设备编号与运行编号的运行 / 已分析运行。"),
                Rate("run_quality_association", "运行—质量关联率", qualityLinked, denominator,
                    "至少关联一条有效检验结果的运行 / 已分析运行。"),
                Rate("analysis_admission", "通用基线条件通过率", eligible, denominator,
                    "生命周期、过程数据、实际参数、单位、最小上下文和质量关联全部满足通用数据可靠性基线要求的运行 / 已分析运行；具体分析仍有各自的证据要求。")
            ],
            ContextFields = ContextFieldsToTrack.Select(field =>
            {
                var present = rows.Count(row => HasContext(row, field));
                return new ContextFieldCoverage
                {
                    Field = field,
                    PresentRunCount = present,
                    RunCount = denominator,
                    Coverage = Divide(present, denominator),
                    RequiredForAdmission = RequiredContextFields.Contains(field, StringComparer.Ordinal)
                };
            }).ToArray(),
            ContextFactors = factorSummaries,
            ContextFactorOverlaps = factorOverlaps,
            UnidentifiableConfoundingCount = factorOverlaps.Count(static item =>
                item.Identifiability == "confounded"),
            Exclusions = exclusions,
            RunImpacts = runImpacts,
            DuplicateTimestampCount = rows.Sum(static row => row.ProcessDataQuality.DuplicateTimestampCount),
            OutOfOrderCount = rows.Sum(static row => row.ProcessDataQuality.OutOfOrderCount),
            SequenceGapCount = rows.Sum(static row => row.ProcessDataQuality.SequenceGapCount),
            MaximumSampleGapMs = Maximum(rows.Select(static row => row.ProcessDataQuality.MaximumGapMs)),
            MaximumAbsoluteSourceClockOffsetMs = Maximum(rows.Select(static row =>
                row.ProcessDataQuality.MaximumAbsoluteSourceClockOffsetMs)),
            WorstRunP95PlatformIngestLatencyMs = Maximum(rows.Select(static row =>
                row.ProcessDataQuality.P95PlatformIngestLatencyMs)),
            MaximumPlatformIngestLatencyMs = Maximum(rows.Select(static row =>
                row.ProcessDataQuality.MaximumPlatformIngestLatencyMs)),
            NegativePlatformIngestLatencyCount = rows.Sum(static row =>
                row.ProcessDataQuality.NegativePlatformIngestLatencyCount)
        };
    }

    private static double? Maximum(IEnumerable<double?> values)
    {
        var present = values.Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .ToArray();
        return present.Length == 0 ? null : present.Max();
    }

    private static Dictionary<string, DateTimeOffset?> LifecycleReceiptTimesByExecution(
        IReadOnlyList<PlatformProductionEvent> lifecycleEvents,
        bool earliest)
        => lifecycleEvents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId))
            .GroupBy(static item => item.Event.ExecutionId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group =>
                {
                    var ordered = earliest
                        ? group.OrderBy(static item => item.Event.OccurredAt)
                            .ThenBy(static item => item.IngestId)
                        : group.OrderByDescending(static item => item.Event.OccurredAt)
                            .ThenByDescending(static item => item.IngestId);
                    return (DateTimeOffset?)ordered.First().IngestedAt;
                },
                StringComparer.Ordinal);

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        CancellationToken ct)
    {
        var result = new List<PlatformProductionEvent>();
        var cursor = 0L;
        while (true)
        {
            var page = await events.QueryAsync(query with { AfterIngestId = cursor, Limit = 500 }, ct)
                .ConfigureAwait(false);
            if (page.Count == 0)
                break;
            if (result.Count + page.Count > MaximumScannedEvents)
            {
                throw new InspectionQueryLimitExceededException(
                    $"数据可靠性基线超过 {MaximumScannedEvents} 条事件扫描上限，请缩小站点或时间范围。");
            }
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("数据可靠性基线查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }

    private static bool HasActualParameters(ExecutionComparisonRow row)
        => row.ControlParameters.Count > 0;

    private IReadOnlyList<string> AdmissionExclusionCodes(ExecutionComparisonRow row)
    {
        var reasons = new List<string>(AdmissionExclusionReasons.Length);
        if (!row.LifecycleComplete)
            reasons.Add("lifecycle_incomplete");
        if (row.ProcessDataQuality.Status != ProcessDataStatuses.Available)
            reasons.Add("process_data_incomplete");
        if (!HasActualParameters(row))
            reasons.Add("actual_parameters_missing");
        if (!HasCompleteActualParameterUnits(row))
            reasons.Add("parameter_unit_missing");
        if (!_contextAdmission.Evaluate(row.Context, null).Admitted)
            reasons.Add("context_capture_invalid");
        if (!HasMinimalContext(row))
            reasons.Add("minimal_context_missing");
        if (row.InspectionOutcomes.Count == 0)
            reasons.Add("quality_unlinked");
        return reasons;
    }

    private static bool HasCompleteActualParameterUnits(ExecutionComparisonRow row)
        => HasActualParameters(row) &&
           row.ControlParameters.All(static value => !string.IsNullOrWhiteSpace(value.Unit));

    private bool HasMinimalContext(ExecutionComparisonRow row)
        => _contextAdmission.Evaluate(row.Context, null).Admitted &&
           RequiredContextFields.All(field => HasContext(row, field));

    private static bool HasContext(ExecutionComparisonRow row, string field)
    {
        if (string.Equals(field, "material_lot_ref", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(
                       ProcessAnalysisResolver.ContextValue(row.Context, field)) ||
                   !string.IsNullOrWhiteSpace(
                       ProcessAnalysisResolver.ContextValue(row.Context, "material_lot"));
        }
        return !string.IsNullOrWhiteSpace(
            ProcessAnalysisResolver.ContextValue(row.Context, field));
    }

    private static IReadOnlyList<ContextFactorSummary> BuildFactorSummaries(
        IReadOnlyList<ExecutionComparisonRow> rows)
        => ContextFactors.Select(factor =>
        {
            var populated = rows
                .Select(row => (Row: row, Value: ResolveFactor(row, factor.Field)))
                .Where(static item => item.Value is not null)
                .ToArray();
            var levels = populated
                .GroupBy(static item => item.Value!, StringComparer.Ordinal)
                .Select(group => BuildFactorLevel(group.Key, group.Select(static item => item.Row).ToArray()))
                .OrderByDescending(static item => item.RunCount)
                .ThenBy(static item => item.Value, StringComparer.Ordinal)
                .ToArray();
            return new ContextFactorSummary
            {
                Field = factor.Field,
                Name = factor.Name,
                PresentRunCount = populated.Length,
                MissingRunCount = rows.Count - populated.Length,
                DistinctLevelCount = levels.Length,
                Coverage = Divide(populated.Length, rows.Count),
                LevelsTruncated = levels.Length > MaximumFactorLevels,
                Levels = levels.Take(MaximumFactorLevels).ToArray()
            };
        }).ToArray();

    private static ContextFactorLevelSummary BuildFactorLevel(
        string value,
        IReadOnlyList<ExecutionComparisonRow> rows)
    {
        var durations = rows
            .Where(static row => row.CompletedAt.HasValue && row.CompletedAt > row.StartedAt)
            .Select(static row => (row.CompletedAt!.Value - row.StartedAt).TotalMilliseconds)
            .ToArray();
        return new ContextFactorLevelSummary
        {
            Value = value,
            RunCount = rows.Count,
            ProcessCompleteRunCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Available),
            QualityLinkedRunCount = rows.Count(static row => row.InspectionOutcomes.Count > 0),
            PassRunCount = rows.Count(static row => QualityOutcome(row) == "PASS"),
            FailRunCount = rows.Count(static row => QualityOutcome(row) == "FAIL"),
            InconclusiveRunCount = rows.Count(static row => QualityOutcome(row) == "INCONCLUSIVE"),
            MeanDurationMs = durations.Length == 0 ? null : durations.Average()
        };
    }

    private static IReadOnlyList<ContextFactorOverlap> BuildFactorOverlaps(
        IReadOnlyList<ExecutionComparisonRow> rows)
    {
        var result = new List<ContextFactorOverlap>();
        for (var leftIndex = 0; leftIndex < ContextFactors.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < ContextFactors.Length; rightIndex++)
            {
                var left = ContextFactors[leftIndex].Field;
                var right = ContextFactors[rightIndex].Field;
                var pairs = rows.Select(row => (
                        Left: ResolveFactor(row, left),
                        Right: ResolveFactor(row, right)))
                    .Where(static pair => pair.Left is not null && pair.Right is not null)
                    .Select(static pair => (Left: pair.Left!, Right: pair.Right!))
                    .ToArray();
                var leftLevels = pairs.Select(static pair => pair.Left).Distinct(StringComparer.Ordinal).ToArray();
                var rightLevels = pairs.Select(static pair => pair.Right).Distinct(StringComparer.Ordinal).ToArray();
                var observed = pairs.Distinct().ToArray();
                var possible = leftLevels.Length * rightLevels.Length;
                var leftNested = leftLevels.Length > 0 && pairs
                    .GroupBy(static pair => pair.Left, StringComparer.Ordinal)
                    .All(static group => group.Select(static pair => pair.Right)
                        .Distinct(StringComparer.Ordinal).Count() == 1);
                var rightNested = rightLevels.Length > 0 && pairs
                    .GroupBy(static pair => pair.Right, StringComparer.Ordinal)
                    .All(static group => group.Select(static pair => pair.Left)
                        .Distinct(StringComparer.Ordinal).Count() == 1);
                var identifiability = leftLevels.Length < 2 || rightLevels.Length < 2
                    ? "insufficient_levels"
                    : leftNested || rightNested
                        ? "confounded"
                        : observed.Length == possible
                            ? "overlapping"
                            : "limited";
                result.Add(new ContextFactorOverlap
                {
                    LeftField = left,
                    RightField = right,
                    JointRunCount = pairs.Length,
                    LeftLevelCount = leftLevels.Length,
                    RightLevelCount = rightLevels.Length,
                    ObservedCombinationCount = observed.Length,
                    PossibleCombinationCount = possible,
                    OverlapRate = Divide(observed.Length, possible),
                    Identifiability = identifiability
                });
            }
        }
        return result;
    }

    private static string? ResolveFactor(ExecutionComparisonRow row, string field)
    {
        var value = field switch
        {
            "equipment_id" => Normalize(ProcessAnalysisResolver.ContextValue(
                                      row.Context, "equipment_id")) ?? Normalize(row.EquipmentId),
            "tooling_assembly_id" => ProcessAnalysisResolver.ContextValue(
                                row.Context, "tooling_assembly_id") ??
                            ProcessAnalysisResolver.ContextValue(row.Context, "tooling_assembly_id"),
            "material_lot_ref" => ProcessAnalysisResolver.ContextValue(
                                      row.Context, "material_lot_ref") ??
                                  ProcessAnalysisResolver.ContextValue(row.Context, "material_lot"),
            _ => ProcessAnalysisResolver.ContextValue(row.Context, field)
        };
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? QualityOutcome(ExecutionComparisonRow row)
        => row.InspectionOutcomes.Contains("FAIL", StringComparer.OrdinalIgnoreCase)
            ? "FAIL"
            : row.InspectionOutcomes.Contains("INCONCLUSIVE", StringComparer.OrdinalIgnoreCase)
                ? "INCONCLUSIVE"
                : row.InspectionOutcomes.Contains("PASS", StringComparer.OrdinalIgnoreCase)
                    ? "PASS"
                    : null;

    private static ReliabilityRate Rate(
        string code,
        string name,
        int numerator,
        int denominator,
        string definition)
        => new()
        {
            Code = code,
            Name = name,
            Numerator = numerator,
            Denominator = denominator,
            Rate = Divide(numerator, denominator),
            Definition = definition
        };

    private static ReliabilityExclusionCount Exclusion(string code, string name, int count)
        => new() { Code = code, Name = name, RunCount = count };

    private static double? Divide(int numerator, int denominator)
        => denominator == 0 ? null : (double)numerator / denominator;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
