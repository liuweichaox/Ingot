using System.Text.Json;
using Ingot.Agent;
using Ingot.Central.Infrastructure.Events;
using Ingot.Central.Infrastructure.Inspections;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;

namespace Ingot.Central.Infrastructure.AgentTools;

public sealed class CompareCyclesTool(
    IChatEventReader events,
    IInspectionRecordStore inspections) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "compare_cycles",
        Version = "1.0.0",
        Surface = ProductSurfaces.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "比较基准周期与一组候选周期的事件序列、检测结果和数值特性差异。只读。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "baselineCorrelationId", "candidateCorrelationIds" },
            properties = new
            {
                baselineCorrelationId = new { type = "string", minLength = 1, maxLength = 200 },
                candidateCorrelationIds = new { type = "string", minLength = 1, maxLength = 4000 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var baselineId = Require(call, "baselineCorrelationId").Trim();
        var candidateIds = Require(call, "candidateCorrelationIds")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(id => !string.Equals(id, baselineId, StringComparison.Ordinal))
            .Take(200)
            .ToArray();
        if (candidateIds.Length == 0)
            throw new ArgumentException("compare_cycles 至少需要一个候选周期。", nameof(call));

        var baseline = await LoadCycleAsync(context.ActorId, baselineId, ct).ConfigureAwait(false);
        var candidates = new List<CycleSnapshot>();
        foreach (var candidateId in candidateIds)
            candidates.Add(await LoadCycleAsync(context.ActorId, candidateId, ct).ConfigureAwait(false));

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
            candidateMeanMs = candidateDurations.Length == 0 ? (double?)null : candidateDurations.Average(),
            candidateP50Ms = Percentile(candidateDurations, 0.5),
            candidateP90Ms = Percentile(candidateDurations, 0.9)
        };
        var baselinePassRate = PassRate(baselineInspections);
        var candidatePassRate = PassRate(candidateInspections);
        var limitations = new List<string>();
        if (baseline.RowsReachedLimit || candidates.Any(static item => item.RowsReachedLimit))
            limitations.Add("至少一个周期命中 500 条事件窗口上限，事件序列差异可能被截断。");
        if (baselineInspections.Count == 500 || candidateInspections.Count >= candidateIds.Length * 500)
            limitations.Add("检测记录查询达到窗口上限，合格率和测量统计可能被截断。");
        if (baseline.Events == 0)
            limitations.Add("基准周期没有生产事件。");
        if (candidates.Any(static item => item.Events == 0))
            limitations.Add("部分候选周期没有生产事件。");

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = $"已比较基准周期 {baselineId} 与 {candidateIds.Length} 个候选周期：基准检测合格率 {FormatRate(baselinePassRate)}，候选检测合格率 {FormatRate(candidatePassRate)}。",
            Data = JsonSerializer.SerializeToElement(new
            {
                baselineCorrelationId = baselineId,
                candidateCorrelationIds = candidateIds,
                eventSequence = new
                {
                    baselineEventCount = baseline.Events,
                    candidateEventCount = candidates.Sum(static item => item.Events),
                    eventTypesOnlyInBaseline = onlyBaseline,
                    eventTypesOnlyInCandidates = onlyCandidates
                },
                duration = durationStats,
                inspection = new
                {
                    baselineRecords = baselineInspections.Count,
                    candidateRecords = candidateInspections.Count,
                    baselinePassRate,
                    candidatePassRate,
                    characteristics = comparison
                }
            }),
            Artifacts =
            [
                new AnalysisArtifactRef
                {
                    Kind = "event-query",
                    Label = "基准周期完整事件",
                    Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(baselineId)}&limit=500"
                },
                new AnalysisArtifactRef
                {
                    Kind = "inspection-query",
                    Label = "基准周期检测记录",
                    Url = $"/api/v1/inspection-records?operationRunId={Uri.EscapeDataString(baselineId)}&limit=500"
                }
            ],
            Evidence = BuildEvidence(baselineId, candidateIds),
            Limitations = limitations,
            Outcome = baseline.Events > 0 && candidates.Any(static item => item.Events > 0)
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }

    private async Task<CycleSnapshot> LoadCycleAsync(string actorId, string correlationId, CancellationToken ct)
    {
        var rows = await events.QueryAsync(
            actorId,
            new CentralEventQuery { CorrelationId = correlationId, Limit = 500 },
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
                    baselineCount = left.Count,
                    candidateCount = right.Count,
                    baselineMean = leftMean,
                    candidateMean = rightMean,
                    meanDifference = leftMean.HasValue && rightMean.HasValue ? rightMean - leftMean : null,
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

    private static IReadOnlyList<EvidenceRef> BuildEvidence(string baselineId, IReadOnlyList<string> candidateIds)
        =>
        [
            new EvidenceRef
            {
                Kind = "event-query",
                Id = $"correlation:{baselineId}",
                Label = $"基准周期 {baselineId}",
                Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(baselineId)}&limit=500"
            },
            .. candidateIds.Take(20).Select(id => new EvidenceRef
            {
                Kind = "event-query",
                Id = $"correlation:{id}",
                Label = $"候选周期 {id}",
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

