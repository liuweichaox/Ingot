using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class CompareCyclesTool(
    IChatEventReader events,
    IInspectionRecordStore inspections) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "compare_cycles",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "比较一个生产周期与一组同类周期的过程、检测结果和参数差异。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "baselineCycleId", "comparisonCycleIds" },
            properties = new
            {
                baselineCycleId = new { type = "string", minLength = 1, maxLength = 200 },
                comparisonCycleIds = new { type = "string", minLength = 1, maxLength = 4000 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var baselineId = Require(call, "baselineCycleId").Trim();
        var candidateIds = Require(call, "comparisonCycleIds")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !string.Equals(id, baselineId, StringComparison.Ordinal))
            .Take(200)
            .ToArray();
        if (candidateIds.Length == 0)
            throw new ArgumentException("compare_cycles 至少需要一个对比周期。", nameof(call));

        var baseline = await LoadCycleAsync(context.UserId, baselineId, ct).ConfigureAwait(false);
        var candidates = new List<CycleSnapshot>();
        foreach (var candidateId in candidateIds)
            candidates.Add(await LoadCycleAsync(context.UserId, candidateId, ct).ConfigureAwait(false));

        var baselineInspections = await inspections.QueryAsync(
            new InspectionRecordQuery { OperationRunId = baselineId, Limit = 500 },
            ct).ConfigureAwait(false);
        var candidateInspections = new List<InspectionRecord>();
        foreach (var candidateId in candidateIds)
        {
            var records = await inspections.QueryAsync(
                new InspectionRecordQuery { OperationRunId = candidateId, Limit = 500 },
                ct).ConfigureAwait(false);
            candidateInspections.AddRange(records);
        }

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
        var limitations = new List<string>();
        if (baseline.RowsReachedLimit || candidates.Any(static item => item.RowsReachedLimit))
            limitations.Add("至少一个周期超过 500 条生产记录，过程差异可能没有完全显示。");
        if (baselineInspections.Count == 500 || candidateInspections.Count >= candidateIds.Length * 500)
            limitations.Add("检测记录查询达到窗口上限，合格率和测量统计可能被截断。");
        if (baseline.Events == 0)
            limitations.Add("基准周期没有生产记录。");
        if (candidates.Any(static item => item.Events == 0))
            limitations.Add("部分同类周期没有生产记录。");

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"已比较基准周期 {baselineId} 与 {candidateIds.Length} 个对比周期：基准检测合格率 {FormatRate(baselinePassRate)}，对比周期检测合格率 {FormatRate(candidatePassRate)}。",
            Data = JsonSerializer.SerializeToElement(new
            {
                baselineCycleId = baselineId,
                comparisonCycleIds = candidateIds,
                eventSequence = new
                {
                    baselineProductionRecordCount = baseline.Events,
                    comparisonProductionRecordCount = candidates.Sum(static item => item.Events),
                    recordTypesOnlyInBaseline = onlyBaseline,
                    recordTypesOnlyInComparison = onlyCandidates
                },
                duration = durationStats,
                inspection = new
                {
                    baselineInspectionCount = baselineInspections.Count,
                    comparisonInspectionCount = candidateInspections.Count,
                    baselinePassRate,
                    comparisonPassRate = candidatePassRate,
                    characteristics = comparison
                }
            }),
            Details =
            [
                new ResultDetailLink
                {
                    Kind = "event-query",
                    Label = "基准周期完整生产记录",
                    Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(baselineId)}&limit=500"
                },
                new ResultDetailLink
                {
                    Kind = "inspection-query",
                    Label = "基准周期检测记录",
                    Url = $"/api/v1/inspection-records?operationRunId={Uri.EscapeDataString(baselineId)}&limit=500"
                }
            ],
            RelatedRecords = BuildRelatedRecords(baselineId, candidateIds),
            Limitations = limitations,
            Outcome = baseline.Events > 0 && candidates.Any(static item => item.Events > 0)
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }

    private async Task<CycleSnapshot> LoadCycleAsync(string userId, string correlationId, CancellationToken ct)
    {
        var rows = await events.QueryAsync(
            userId,
            new PlatformEventQuery { CorrelationId = correlationId, Limit = 500 },
            ct).ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var startedAt = ordered.FirstOrDefault(static row =>
            row.Event.EventType.EndsWith(".started", StringComparison.Ordinal))?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(static row =>
            row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".exited", StringComparison.Ordinal))?.Event.OccurredAt;
        return new CycleSnapshot(
            correlationId,
            ordered.Length,
            ordered.Length == 500,
            startedAt,
            completedAt,
            startedAt.HasValue && completedAt.HasValue && completedAt >= startedAt
                ? (completedAt.Value - startedAt.Value).TotalMilliseconds
                : null,
            ordered.Select(static row => row.Event.EventType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
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
                return new
                {
                    characteristicCode = code,
                    baselineSampleCount = left.Count,
                    comparisonSampleCount = right.Count,
                    baselineAverage = leftMean,
                    comparisonAverage = rightMean,
                    averageDifference = leftMean.HasValue && rightMean.HasValue ? rightMean - leftMean : null,
                    effectSize = CohenD(left, right)
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

    private static double? CohenD(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        if (left.Count < 2 || right.Count < 2)
            return null;
        var pooled = Math.Sqrt((Variance(left) + Variance(right)) / 2d);
        return pooled <= 0 ? null : (right.Average() - left.Average()) / pooled;
    }

    private static double Variance(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        return values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
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
                Label = $"基准周期 {baselineId}",
                Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(baselineId)}&limit=500"
            },
            .. candidateIds.Take(20).Select(id => new RelatedRecordRef
            {
                Kind = "event-query",
                Id = $"correlation:{id}",
                Label = $"对比周期 {id}",
                Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(id)}&limit=500"
            })
        ];

    private static string Require(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{call.Tool} 需要 {name}。", nameof(call));

    private static string FormatRate(double? value)
        => value.HasValue ? value.Value.ToString("P1") : "无检测记录";

    private sealed record CycleSnapshot(
        string CorrelationId,
        int Events,
        bool RowsReachedLimit,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        double? DurationMs,
        IReadOnlyList<string> EventTypes);
}
