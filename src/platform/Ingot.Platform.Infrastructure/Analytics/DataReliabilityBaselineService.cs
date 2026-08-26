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
        var maximumRuns = Math.Clamp(query.MaximumRuns, 1, 2000);
        var completedEvents = await QueryAllAsync(new PlatformEventQuery
        {
            SiteId = Normalize(query.SiteId),
            EventType = "process.execution.completed",
            EdgeId = Normalize(query.EdgeId),
            SubjectId = Normalize(query.EquipmentId),
            From = query.From,
            To = query.To
        }, ct).ConfigureAwait(false);
        var matchingIds = completedEvents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId))
            .GroupBy(static item => item.Event.ExecutionId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Select(static item => item.Event.ExecutionId!)
            .ToArray();
        var selectedIds = matchingIds.Take(maximumRuns).ToArray();
        var executionMap = await executions.GetProcessExecutionsAsync(
            selectedIds, ct, Normalize(query.SiteId)).ConfigureAwait(false);
        var rows = selectedIds
            .Where(executionMap.ContainsKey)
            .Select(id => executionMap[id])
            .ToArray();

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
        var eligible = rows.Count(row =>
            row.LifecycleComplete &&
            row.ProcessDataQuality.Status == ProcessDataStatuses.Available &&
            HasActualParameters(row) &&
            HasCompleteActualParameterUnits(row) &&
            HasMinimalContext(row) &&
            row.InspectionOutcomes.Count > 0);

        var exclusions = new[]
        {
            Exclusion("lifecycle_incomplete", "运行生命周期不完整", rows.Count(static row => !row.LifecycleComplete)),
            Exclusion("process_data_incomplete", "过程数据未达到完整标准", denominator - processComplete),
            Exclusion("actual_parameters_missing", "缺少设备实际参数回读", denominator - actualParameters),
            Exclusion("parameter_unit_missing", "实际参数单位不完整", denominator - parameterUnits),
            Exclusion("context_capture_invalid", "生产上下文捕获失败", denominator - contextIntegrity),
            Exclusion("minimal_context_missing", "最小上下文快照不完整", denominator - minimalContext),
            Exclusion("quality_unlinked", "没有关联有效质量结果", denominator - qualityLinked)
        }.Where(static item => item.RunCount > 0).ToArray();

        var factorSummaries = BuildFactorSummaries(rows);
        var factorOverlaps = BuildFactorOverlaps(rows);
        return new DataReliabilityBaseline
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            From = query.From,
            To = query.To,
            EdgeId = Normalize(query.EdgeId),
            EquipmentId = Normalize(query.EquipmentId),
            MatchingCompletedRunCount = matchingIds.Length,
            AnalyzedRunCount = denominator,
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
                    "存在“工艺规范已应用”现场实际参数回读的运行 / 已分析运行；不使用工艺规范计划值。"),
                Rate("actual_parameter_unit_completeness", "实际参数单位完整率", parameterUnits, denominator,
                    "全部实际参数具有明确单位的运行 / 已分析运行。"),
                Rate("context_capture_integrity", "生产上下文捕获可信率", contextIntegrity, denominator,
                    "上下文未标记为“配置缺失”或其他无效捕获状态的运行 / 已分析运行。"),
                Rate("minimal_context_coverage", "最小上下文覆盖率", minimalContext, denominator,
                    "上下文捕获有效且同时包含设备编号与运行编号的运行 / 已分析运行。"),
                Rate("run_quality_association", "运行—质量关联率", qualityLinked, denominator,
                    "至少关联一条有效检验结果的运行 / 已分析运行。"),
                Rate("analysis_admission", "正式分析准入率", eligible, denominator,
                    "生命周期、过程数据、实际参数、单位、最小上下文和质量关联全部通过的运行 / 已分析运行。")
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
            "equipment_id" => ProcessAnalysisResolver.ContextValue(
                                  row.Context, "equipment_id") ?? row.EquipmentId,
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
