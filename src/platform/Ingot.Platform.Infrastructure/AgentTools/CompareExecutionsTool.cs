using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class CompareExecutionsTool(
    IChatEventReader events,
    IInspectionRecordStore inspections,
    IExecutionComparisonService? executionComparisons = null,
    IInspectionReviewStore? reviews = null,
    IInspectionMasterDataStore? inspectionMasterData = null) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "compare_executions",
        Version = "1.1.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "比较一个生产过程执行与一组同类过程执行的过程、检测结果和参数差异。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "baselineProcessExecutionId", "comparisonProcessExecutionIds" },
            properties = new
            {
                baselineProcessExecutionId = new { type = "string", minLength = 1, maxLength = 200 },
                comparisonProcessExecutionIds = new { type = "string", minLength = 1, maxLength = 4000 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var baselineId = Require(call, "baselineProcessExecutionId").Trim();
        var candidateIds = Require(call, "comparisonProcessExecutionIds")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !string.Equals(id, baselineId, StringComparison.Ordinal))
            .Take(200)
            .ToArray();
        if (candidateIds.Length == 0)
            throw new ArgumentException("compare_executions 至少需要一个对比过程执行。", nameof(call));

        var baseline = await LoadProcessExecutionAsync(context.UserId, baselineId, ct).ConfigureAwait(false);
        var candidates = new List<ProcessExecutionSnapshot>();
        foreach (var candidateId in candidateIds)
            candidates.Add(await LoadProcessExecutionAsync(context.UserId, candidateId, ct).ConfigureAwait(false));

        var allInspections = InspectionRecordSet.Effective(
            await inspections.QueryAllByExecutionIdsAsync(
                [baselineId, .. candidateIds],
                ct).ConfigureAwait(false));
        var latestReviews = reviews is null
            ? new Dictionary<Guid, InspectionReview>()
            : await reviews.GetLatestByInspectionRecordIdsAsync(
                allInspections.Select(static value => value.RecordId).ToArray(), ct).ConfigureAwait(false);
        var plans = inspectionMasterData is null
            ? []
            : await inspectionMasterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var baselineInspections = EligibleInspections(
            allInspections.Where(record => string.Equals(
                record.ExecutionId, baseline.ExecutionId, StringComparison.Ordinal)),
            baseline,
            plans,
            latestReviews);
        var candidateInspections = candidates.SelectMany(candidate => EligibleInspections(
                allInspections.Where(record => string.Equals(
                    record.ExecutionId, candidate.ExecutionId, StringComparison.Ordinal)),
                candidate,
                plans,
                latestReviews))
            .ToArray();

        var comparison = BuildMeasurementComparison(baselineInspections, candidateInspections);
        var baselineEventTypes = baseline.EventTypes.ToHashSet(StringComparer.Ordinal);
        var candidateEventTypes = candidates.SelectMany(static item => item.EventTypes).ToHashSet(StringComparer.Ordinal);
        var onlyBaseline = baselineEventTypes.Except(candidateEventTypes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var onlyCandidates = candidateEventTypes.Except(baselineEventTypes, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var candidateDurations = candidates.Where(static item => item.DurationMs.HasValue)
            .Select(static item => item.DurationMs!.Value)
            .ToArray();
        var durationStats = new
        {
            baseline.DurationMs,
            comparisonAverageMs = candidateDurations.Length == 0 ? (double?)null : candidateDurations.Average(),
            comparisonMedianMs = Percentile(candidateDurations, 0.5),
            comparisonP90Ms = Percentile(candidateDurations, 0.9)
        };
        var baselinePassRate = PassRate(baselineInspections);
        var candidatePassRate = PassRate(candidateInspections);
        var processComparison = executionComparisons is null
            ? null
            : await executionComparisons.CompareSelectedAsync(
                baselineId,
                [baselineId, .. candidateIds],
                ct).ConfigureAwait(false);
        var limitations = new List<string>();
        if (baseline.Events == 0)
            limitations.Add("基准过程执行没有生产记录。");
        if (candidates.Any(static item => item.Events == 0))
            limitations.Add("部分同类过程执行没有生产记录。");
        if (processComparison is null)
            limitations.Add("没有匹配的已发布分析方案，未生成工艺信号和控制参数比较。");
        else if (processComparison.EvidenceLevel == "insufficient")
            limitations.Add("可用过程数据的有效过程执行权重不足 5，本次比较只能用于查看记录，不能形成工艺结论。");
        else if (processComparison.EvidenceLevel == "exploratory")
            limitations.Add("可用过程数据的有效过程执行权重不足 20，本次结果属于探索性比较。");
        if (processComparison is not null && processComparison.QualityAssociations.Count == 0)
            limitations.Add("合格组或不合格组样本不足，未生成质量关联候选因素。");
        var topCandidate = processComparison?.QualityAssociations
            .FirstOrDefault(static item => item.CandidateScore > 0);
        if (topCandidate?.PossibleConfounders.Count > 0)
            limitations.Add($"首要候选仍可能受 {string.Join("、", topCandidate.PossibleConfounders)} 等分组差异影响。");
        limitations.Add("过程数据差异仅表示关联，不能据此直接认定原因或给出调参幅度。");
        var details = new List<ResultDetailLink>
        {
            new()
            {
                Kind = "event-query",
                Label = "基准过程执行生产记录明细（分页）",
                Url = $"/api/v1/events?executionId={Uri.EscapeDataString(baselineId)}&limit=500"
            },
            new()
            {
                Kind = "inspection-query",
                Label = "基准过程执行检测记录明细（分页）",
                Url = $"/api/v1/inspection-records?executionId={Uri.EscapeDataString(baselineId)}&limit=500"
            }
        };
        if (topCandidate is not null)
        {
            details.Add(new ResultDetailLink
            {
                Kind = "process-phase-evidence",
                Label = $"首要候选阶段证据：{topCandidate.SignalCode} / {topCandidate.PhaseName ?? topCandidate.PhaseCode ?? "整次执行"} / {topCandidate.FeatureCode}",
                Url = $"/api/v1/process-executions/{Uri.EscapeDataString(baselineId)}"
            });
        }

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"已比较基准过程执行 {baselineId} 与 {candidateIds.Length} 个对比过程执行：证据等级 {processComparison?.EvidenceLevel ?? "insufficient"}，基准检测合格率 {FormatRate(baselinePassRate)}，对比过程执行检测合格率 {FormatRate(candidatePassRate)}。{FormatCandidate(topCandidate)}",
            Data = JsonSerializer.SerializeToElement(new
            {
                baselineProcessExecutionId = baselineId,
                comparisonProcessExecutionIds = candidateIds,
                eventSequence = new
                {
                    baselineProductionRecordCount = baseline.Events,
                    comparisonProductionRecordCount = candidates.Sum(static item => item.Events),
                    recordTypesOnlyInBaseline = onlyBaseline,
                    recordTypesOnlyInComparison = onlyCandidates
                },
                duration = durationStats,
                process = processComparison,
                inspection = new
                {
                    baselineInspectionCount = baselineInspections.Count,
                    comparisonInspectionCount = candidateInspections.Length,
                    baselinePassRate,
                    comparisonPassRate = candidatePassRate,
                    characteristics = comparison
                }
            }),
            Details = details,
            RelatedRecords = BuildRelatedRecords(baselineId, candidateIds),
            Limitations = limitations,
            Outcome = baseline.Events > 0 && candidates.Any(static item => item.Events > 0) &&
                      processComparison?.EvidenceLevel is "exploratory" or "stable"
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }

    private async Task<ProcessExecutionSnapshot> LoadProcessExecutionAsync(string userId, string executionId, CancellationToken ct)
    {
        var rows = await events.QueryAllAsync(
            userId,
            new PlatformEventQuery { ExecutionId = executionId },
            ct).ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var startedAt = ordered.FirstOrDefault(static row =>
            row.Event.EventType.EndsWith(".started", StringComparison.Ordinal))?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(static row =>
            row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".exited", StringComparison.Ordinal))?.Event.OccurredAt;
        return new ProcessExecutionSnapshot(
            executionId,
            ordered.Length,
            startedAt,
            completedAt,
            startedAt.HasValue && completedAt.HasValue && completedAt >= startedAt
                ? (completedAt.Value - startedAt.Value).TotalMilliseconds
                : null,
            ordered.Select(static row => row.Event.EventType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ordered.Select(static row => row.Event.Context).FirstOrDefault(static value => value.Count > 0)
                ?? new Dictionary<string, string>(),
            ordered.FirstOrDefault()?.Event.Subject.Id ?? "unknown");
    }

    private static IReadOnlyList<InspectionRecord> EligibleInspections(
        IEnumerable<InspectionRecord> records,
        ProcessExecutionSnapshot execution,
        IReadOnlyList<InspectionPlan> plans,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews)
    {
        var plan = InspectionPlanMatcher.Resolve(
            plans,
            execution.Context,
            execution.EquipmentId,
            execution.StartedAt ?? DateTimeOffset.MinValue);
        return InspectionRecordSet.AnalysisEligible(records, plan, latestReviews);
    }

    private static IReadOnlyList<object> BuildMeasurementComparison(
        IReadOnlyList<InspectionRecord> baseline,
        IReadOnlyList<InspectionRecord> candidates)
    {
        var baselineValues = NumericValues(baseline);
        var candidateValues = NumericValues(candidates);
        return baselineValues.Keys.Concat(candidateValues.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(code =>
            {
                var left = baselineValues.GetValueOrDefault(code, []);
                var right = candidateValues.GetValueOrDefault(code, []);
                var leftMean = left.Count == 0 ? (double?)null : left.Average();
                var rightMean = right.Count == 0 ? (double?)null : right.Average();
                var comparisonMedian = Percentile(right, 0.5);
                var mad = comparisonMedian.HasValue
                    ? Percentile(right.Select(value => Math.Abs(value - comparisonMedian.Value)).ToArray(), 0.5)
                    : null;
                return new
                {
                    characteristicCode = code,
                    baselineSampleCount = left.Count,
                    comparisonSampleCount = right.Count,
                    baselineAverage = leftMean,
                    comparisonAverage = rightMean,
                    averageDifference = leftMean.HasValue && rightMean.HasValue ? rightMean - leftMean : null,
                    comparisonMedian,
                    comparisonP10 = Percentile(right, 0.1),
                    comparisonP90 = Percentile(right, 0.9),
                    baselinePercentile = leftMean.HasValue && right.Count > 0
                        ? right.Count(value => value <= leftMean.Value) / (double)right.Count
                        : (double?)null,
                    robustDeviation = leftMean.HasValue && comparisonMedian.HasValue && mad is > 0
                        ? (leftMean.Value - comparisonMedian.Value) / (1.4826d * mad.Value)
                        : (double?)null
                };
            })
            .Cast<object>()
            .Take(50)
            .ToArray();
    }

    private static Dictionary<string, List<double>> NumericValues(IEnumerable<InspectionRecord> records)
    {
        var values = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var measurement in records.SelectMany(static record => record.Measurements))
        {
            if (!measurement.NumericValue.HasValue)
                continue;
            if (!values.TryGetValue(measurement.CharacteristicCode, out var bucket))
                values[measurement.CharacteristicCode] = bucket = [];
            bucket.Add((double)measurement.NumericValue.Value);
        }

        return values;
    }

    private static double? Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
            return null;
        var ordered = values.Order().ToArray();
        var position = (ordered.Length - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return ordered[lower];
        return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower);
    }

    private static double? PassRate(IReadOnlyList<InspectionRecord> records)
        => records.Count == 0
            ? null
            : records.Count(static record => record.Outcome == "PASS") / (double)records.Count;

    private static IReadOnlyList<RelatedRecordRef> BuildRelatedRecords(string baselineId, IReadOnlyList<string> candidateIds)
        =>
        [
            new RelatedRecordRef
            {
                Kind = "event-query",
                Id = $"correlation:{baselineId}",
                Label = $"基准过程执行 {baselineId}",
                Url = $"/api/v1/events?executionId={Uri.EscapeDataString(baselineId)}&limit=500"
            },
            .. candidateIds.Take(20).Select(id => new RelatedRecordRef
            {
                Kind = "event-query",
                Id = $"correlation:{id}",
                Label = $"对比过程执行 {id}",
                Url = $"/api/v1/events?executionId={Uri.EscapeDataString(id)}&limit=500"
            })
        ];

    private static string Require(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{call.Tool} 需要 {name}。", nameof(call));

    private static string FormatRate(double? value)
        => value.HasValue ? value.Value.ToString("P1") : "无检测记录";

    private static string FormatCandidate(ExecutionQualityAssociation? candidate)
        => candidate is null
            ? "当前没有可排序的质量关联候选因素。"
            : $"首要候选为 {candidate.SignalCode} / {candidate.PhaseName ?? candidate.PhaseCode ?? "整次执行"} / {candidate.FeatureCode}，候选分数 {candidate.CandidateScore:F3}；该排序不等于已确认根因。";

    private sealed record ProcessExecutionSnapshot(
        string ExecutionId,
        int Events,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        double? DurationMs,
        IReadOnlyList<string> EventTypes,
        IReadOnlyDictionary<string, string> Context,
        string EquipmentId);
}
