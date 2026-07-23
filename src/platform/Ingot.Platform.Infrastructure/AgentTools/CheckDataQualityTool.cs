using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class CheckDataQualityTool(IChatEventReader events) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "check_data_quality",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "检查生产周期是否完整、生产信息是否缺失、现场采集是否中断。只查询，不修改数据。",
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
        var scope = new PlatformEventQuery
        {
            SubjectId = NullIfBlank(subjectId),
            CorrelationId = NullIfBlank(correlationId)
        };
        // 全范围聚合用于快速获得总量与时间边界。
        var stats = await events.GetScopeStatsAsync(context.UserId, scope, ct).ConfigureAwait(false);
        // 明细检查自动翻页读取完整范围；500 只是内部单页传输大小，不是分析上限。
        var rows = await events.QueryAllAsync(
            context.UserId,
            scope,
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
        var phaseEvents = ordered
            .Where(static row => row.Event.EventType.StartsWith("phase.", StringComparison.Ordinal))
            .ToArray();
        var phaseGroups = phaseEvents
            .Select(static row => new
            {
                Row = row,
                PhaseCode = TryReadPhaseCode(row.Event.EventType)
            })
            .Where(static item => item.PhaseCode is not null)
            .GroupBy(
                static item => new { item.Row.Event.CorrelationId, PhaseCode = item.PhaseCode! },
                item => item.Row)
            .ToArray();
        var incompletePhases = phaseGroups.Count(group =>
            group.Any(static row => row.Event.EventType.EndsWith(".started", StringComparison.Ordinal)) !=
            group.Any(static row => row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal)));
        var phaseOrderIssues = phaseGroups.Count(group =>
        {
            var phaseStarted = group.FirstOrDefault(static row =>
                row.Event.EventType.EndsWith(".started", StringComparison.Ordinal))?.Event.OccurredAt;
            var phaseCompleted = group.LastOrDefault(static row =>
                row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal))?.Event.OccurredAt;
            return phaseStarted.HasValue && phaseCompleted.HasValue && phaseCompleted < phaseStarted;
        });
        var unknownPhaseAttribution = ordered.Count(static row =>
            row.Event.Context.TryGetValue("phase_code", out var phase) &&
            string.Equals(phase, "unknown", StringComparison.OrdinalIgnoreCase));
        var estimatedPhaseCount = ordered.Count(static row =>
            row.Event.Context.TryGetValue("phase_source", out var phaseSource) &&
            string.Equals(phaseSource, "estimated", StringComparison.OrdinalIgnoreCase));
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
        var limitations = new List<string>();
        if (scopeEmpty)
            limitations.Add("当前范围没有生产记录，无法检查周期是否完整或采集是否中断。");
        else if (scopedQuery)
            limitations.Add("按对象或周期过滤后的事件不是完整 Edge 序列，因此不计算序号连续性。");
        if (incompletePhases > 0)
            limitations.Add($"有 {incompletePhases} 个工艺阶段缺少开始或结束记录。");
        if (phaseOrderIssues > 0)
            limitations.Add($"发现 {phaseOrderIssues} 个阶段完成时间早于开始时间。");
        if (unknownPhaseAttribution > 0)
            limitations.Add($"有 {unknownPhaseAttribution} 条生产记录无法归入工艺阶段。");
        if (estimatedPhaseCount > 0)
            limitations.Add($"有 {estimatedPhaseCount} 条记录的工艺阶段由系统估算，分析结果仅作参考。");
        var scopeId = $"events:{subjectId ?? "*"}:{correlationId ?? "*"}:{ordered.FirstOrDefault()?.IngestId ?? 0}-{ordered.LastOrDefault()?.IngestId ?? 0}";
        var relatedRecords = new RelatedRecordRef
        {
            Kind = "event-query",
            Id = scopeId,
            Label = $"生产记录查询结果（已完整检查 {ordered.Length} 条）",
            Url = BuildEventsUrl(subjectId, correlationId)
        };
        var summary = scopeEmpty
            ? "当前范围没有生产记录，无法检查数据完整性。"
            : $"范围内共 {totalEvents} 条生产记录，已完整检查 {ordered.Length} 条：发现 {incompleteCycles} 个不完整生产周期、" +
              (sequenceGaps.HasValue ? $"{sequenceGaps} 个序号间断，" : "序号连续性未在当前过滤范围计算，") +
              $"{emptyContext} 条记录缺少生产信息；最新记录时间为 {latest:O}。";
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
                phaseEventCount = phaseEvents.Length,
                incompletePhases,
                phaseOrderIssues,
                unknownPhaseAttribution,
                estimatedPhaseCount,
                sequenceGaps,
                emptyContext,
                latestOccurredAt = latest,
                earliestOccurredAt = stats.EarliestOccurredAt
            }),
            RelatedRecords = [relatedRecords],
            Limitations = limitations,
            Outcome = scopeEmpty || incompletePhases > 0 || phaseOrderIssues > 0
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
        return values.Count == 0 ? "/events" : $"/events?{string.Join('&', values)}";
    }

    private static string? TryReadPhaseCode(string eventType)
    {
        var parts = eventType.Split('.');
        return parts.Length == 3 && parts[0] == "phase" ? parts[1] : null;
    }
}
