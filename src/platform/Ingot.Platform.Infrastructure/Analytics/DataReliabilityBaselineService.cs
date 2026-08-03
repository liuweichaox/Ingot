using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;

namespace Ingot.Platform.Infrastructure.Analytics;

public interface IDataReliabilityBaselineService
{
    Task<DataReliabilityBaseline> CalculateAsync(
        DataReliabilityBaselineQuery query,
        CancellationToken ct = default);
}

public sealed class DataReliabilityBaselineService(
    IPlatformEventStore events,
    ICycleComparisonService cycles) : IDataReliabilityBaselineService
{
    private static readonly string[] ContextFieldsToTrack =
    [
        "equipment_id",
        "operation_run_id",
        "material_lot",
        "material_specification",
        "tooling_id",
        "mold_id",
        "assembly_revision_id",
        "maintenance_status",
        "calibration_status"
    ];

    private static readonly string[] RequiredContextFields = ["equipment_id", "operation_run_id"];

    public async Task<DataReliabilityBaseline> CalculateAsync(
        DataReliabilityBaselineQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To)
            throw new ArgumentException("开始时间不能晚于结束时间。", nameof(query));
        var maximumRuns = Math.Clamp(query.MaximumRuns, 1, 5000);
        var completedEvents = await QueryAllAsync(new PlatformEventQuery
        {
            EventType = "cycle.completed",
            EdgeId = Normalize(query.EdgeId),
            SubjectId = Normalize(query.EquipmentId),
            From = query.From,
            To = query.To
        }, ct).ConfigureAwait(false);
        var matchingIds = completedEvents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.CorrelationId))
            .GroupBy(static item => item.Event.CorrelationId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Select(static item => item.Event.CorrelationId!)
            .ToArray();
        var selectedIds = matchingIds.Take(maximumRuns).ToArray();
        var cycleMap = await cycles.GetCyclesAsync(selectedIds, ct).ConfigureAwait(false);
        var rows = selectedIds
            .Where(cycleMap.ContainsKey)
            .Select(id => cycleMap[id])
            .ToArray();

        var denominator = rows.Length;
        var lifecycleComplete = rows.Count(static row => row.LifecycleComplete);
        var processComplete = rows.Count(static row =>
            row.ProcessDataQuality.Status == ProcessDataStatuses.Available);
        var processUsable = rows.Count(static row =>
            row.ProcessDataQuality.Status != ProcessDataStatuses.Unavailable);
        var actualParameters = rows.Count(HasActualParameters);
        var parameterUnits = rows.Count(HasCompleteActualParameterUnits);
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
            Exclusion("minimal_context_missing", "最小上下文快照不完整", denominator - minimalContext),
            Exclusion("quality_unlinked", "没有关联有效质量结果", denominator - qualityLinked)
        }.Where(static item => item.RunCount > 0).ToArray();

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
                    "同时存在 cycle.started 与 cycle.completed 的运行 / 已分析运行。"),
                Rate("process_data_completeness", "过程数据完整率", processComplete, denominator,
                    "过程数据状态为 available 的运行 / 已分析运行；degraded 不计为完整。"),
                Rate("process_data_usability", "过程数据可用率", processUsable, denominator,
                    "过程数据状态不是 unavailable 的运行 / 已分析运行。"),
                Rate("actual_parameter_coverage", "实际参数覆盖率", actualParameters, denominator,
                    "存在 recipe.applied 现场实际参数回读的运行 / 已分析运行；不使用配方计划值。"),
                Rate("actual_parameter_unit_completeness", "实际参数单位完整率", parameterUnits, denominator,
                    "全部实际参数具有明确单位的运行 / 已分析运行。"),
                Rate("minimal_context_coverage", "最小上下文覆盖率", minimalContext, denominator,
                    "上下文同时包含 equipment_id 与 operation_run_id 的运行 / 已分析运行。"),
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
            Exclusions = exclusions,
            DuplicateTimestampCount = rows.Sum(static row => row.ProcessDataQuality.DuplicateTimestampCount),
            OutOfOrderCount = rows.Sum(static row => row.ProcessDataQuality.OutOfOrderCount),
            SequenceGapCount = rows.Sum(static row => row.ProcessDataQuality.SequenceGapCount),
            MaximumSampleGapMs = rows.Select(static row => row.ProcessDataQuality.MaximumGapMs).Max()
        };
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

    private static bool HasActualParameters(CycleComparisonRow row)
        => row.RecipeParameters.Count > 0;

    private static bool HasCompleteActualParameterUnits(CycleComparisonRow row)
        => HasActualParameters(row) &&
           row.RecipeParameters.All(static value => !string.IsNullOrWhiteSpace(value.Unit));

    private static bool HasMinimalContext(CycleComparisonRow row)
        => RequiredContextFields.All(field => HasContext(row, field));

    private static bool HasContext(CycleComparisonRow row, string field)
        => !string.IsNullOrWhiteSpace(ProcessConfiguration.ProcessAnalysisResolver.ContextValue(row.Context, field));

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
