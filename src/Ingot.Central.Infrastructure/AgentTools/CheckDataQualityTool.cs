using System.Text.Json;
using Ingot.Agent;
using Ingot.Central.Infrastructure.Events;
using Ingot.Contracts.Agents;

namespace Ingot.Central.Infrastructure.AgentTools;

public sealed class CheckDataQualityTool(IChatEventReader events) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "check_data_quality",
        Version = "1.0.0",
        Surface = ProductSurfaces.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "检查生产事件的周期配对、上下文完整性、边缘序号连续性和数据新鲜度。只读。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                subjectId = new { type = "string" },
                correlationId = new { type = "string" }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        call.Arguments.TryGetValue("subjectId", out var subjectId);
        call.Arguments.TryGetValue("correlationId", out var correlationId);
        var scope = new CentralEventQuery
        {
            SubjectId = NullIfBlank(subjectId),
            CorrelationId = NullIfBlank(correlationId)
        };
        // 全范围聚合：总条数与新鲜度不受 500 明细窗口截断（在 hypertable 上是廉价的时间聚合）。
        var stats = await events.GetScopeStatsAsync(context.ActorId, scope, ct).ConfigureAwait(false);
        // 明细窗口：周期配对 / 序号连续性 / 上下文缺失需要逐行数据，仍限 500 行。
        var rows = await events.QueryAsync(
            context.ActorId,
            scope with { Limit = 500 },
            ct).ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.IngestId).ToArray();
        var emptyContext = ordered.Count(static row => row.Event.Context.Count == 0);
        var correlations = ordered
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.CorrelationId))
            .GroupBy(static row => row.Event.CorrelationId!, StringComparer.Ordinal)
            .ToArray();
        var incompleteCycles = correlations.Count(group =>
            group.Any(static row => row.Event.EventType.EndsWith(".started", StringComparison.Ordinal)) !=
            group.Any(static row =>
                row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
                row.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
                row.Event.EventType.EndsWith(".exited", StringComparison.Ordinal)));
        var scopedQuery = !string.IsNullOrWhiteSpace(subjectId) || !string.IsNullOrWhiteSpace(correlationId);
        int? sequenceGaps = null;
        if (!scopedQuery)
        {
            sequenceGaps = ordered
                .GroupBy(static row => row.EdgeId, StringComparer.OrdinalIgnoreCase)
                .Sum(static group =>
                {
                    var sequences = group.Select(static row => row.Event.Seq).Distinct().Order().ToArray();
                    return sequences.Zip(sequences.Skip(1)).Count(static pair => pair.Second > pair.First + 1);
                });
        }
        // 新鲜度取自全范围聚合，不再受 500 明细窗口影响（即使乱序回填也能反映真实最新时间）。
        var latest = stats.LatestOccurredAt;
        var totalEvents = stats.Count;
        var scopeEmpty = totalEvents == 0;
        var reachedResultLimit = ordered.Length == 500;
        var limitations = new List<string>();
        if (reachedResultLimit)
            limitations.Add("周期配对、序号连续性等明细检查达到 500 条窗口上限，无法确认窗口外的明细质量（总量与新鲜度已按全范围统计）。");
        if (scopeEmpty)
            limitations.Add("当前范围没有生产事件，无法判断周期配对和序号连续性。");
        else if (scopedQuery)
            limitations.Add("按对象或周期过滤后的事件不是完整 Edge 序列，因此不计算序号连续性。");
        var scopeId = $"events:{subjectId ?? "*"}:{correlationId ?? "*"}:{ordered.FirstOrDefault()?.IngestId ?? 0}-{ordered.LastOrDefault()?.IngestId ?? 0}";
        var evidence = new EvidenceRef
        {
            Kind = "event-query",
            Id = scopeId,
            Label = $"生产事件查询窗口（明细 {ordered.Length} 条 / 范围内共 {totalEvents} 条）",
            Url = BuildEventsUrl(subjectId, correlationId)
        };
        var summary = scopeEmpty
            ? "当前范围没有生产事件，无法确认数据质量。"
            : $"范围内共 {totalEvents} 条事件，抽检 {ordered.Length} 条：发现 {incompleteCycles} 个未配对关联组、" +
              (sequenceGaps.HasValue ? $"{sequenceGaps} 个序号间断，" : "序号连续性未在当前过滤范围计算，") +
              $"{emptyContext} 条事件没有上下文；最新事件时间为 {latest:O}。";
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new
            {
                eventCount = ordered.Length,
                totalEventCount = totalEvents,
                correlationCount = correlations.Length,
                incompleteCycles,
                sequenceGaps,
                emptyContext,
                latestOccurredAt = latest,
                earliestOccurredAt = stats.EarliestOccurredAt
            }),
            Evidence = [evidence],
            Limitations = limitations,
            Outcome = scopeEmpty || reachedResultLimit
                ? AnalysisToolOutcomes.InsufficientData
                : AnalysisToolOutcomes.Sufficient
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildEventsUrl(string? subjectId, string? correlationId)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(subjectId))
            values.Add($"subjectId={Uri.EscapeDataString(subjectId)}");
        if (!string.IsNullOrWhiteSpace(correlationId))
            values.Add($"correlationId={Uri.EscapeDataString(correlationId)}");
        values.Add("limit=500");
        return $"/api/v1/events?{string.Join('&', values)}";
    }
}
