using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class GetCycleTraceTool(IChatEventReader events) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "get_cycle_trace",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "按生产周期号还原一次完整生产过程。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "correlationId" },
            properties = new { correlationId = new { type = "string", minLength = 1, maxLength = 200 } },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        if (!call.Arguments.TryGetValue("correlationId", out var correlationId) ||
            string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("请提供生产周期号。", nameof(call));
        correlationId = correlationId.Trim();
        var rows = await events.QueryAllAsync(
            context.UserId,
            new PlatformEventQuery { CorrelationId = correlationId }, ct)
            .ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId)
            .ToArray();
        var cycleUrl = $"/api/v1/cycles/{Uri.EscapeDataString(correlationId)}";
        RelatedRecordRef[] relatedRecords =
        [
            new RelatedRecordRef
            {
                Kind = "cycle-query",
                Id = $"correlation:{correlationId}",
                Label = $"完整周期 {correlationId}",
                Url = cycleUrl
            }
        ];

        var startedAt = ordered.FirstOrDefault(row =>
            row.Event.EventType.EndsWith(".started", StringComparison.Ordinal))?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(row =>
            row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".exited", StringComparison.Ordinal))?.Event.OccurredAt;
        var durationMs = startedAt is { } start && completedAt is { } completed && completed >= start
            ? (completed - start).TotalMilliseconds
            : (double?)null;
        var validDuration = durationMs.HasValue;
        var limitations = new List<string>();
        if (ordered.Length == 0)
            limitations.Add("当前范围没有生产记录，无法还原该生产周期。");
        if (ordered.Length > 0 && !startedAt.HasValue)
            limitations.Add("没有找到加工开始记录，无法确认周期起点和持续时间。");
        if (ordered.Length > 0 && !completedAt.HasValue)
            limitations.Add("没有找到加工完成记录，无法确认完整持续时间。");
        if (startedAt.HasValue && completedAt.HasValue && completedAt < startedAt)
            limitations.Add("周期完成时间早于开始时间，无法确认有效持续时间。");

        var summary = ordered.Length == 0
            ? $"没有找到生产周期 {correlationId}。"
            : $"生产周期 {correlationId} 包含 {ordered.Length} 条记录" +
              (startedAt.HasValue ? $"，开始于 {startedAt:O}" : "，未发现加工开始记录") +
              (completedAt.HasValue ? $"，完成于 {completedAt:O}。" : "，未发现加工完成记录。");
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new
            {
                correlationId,
                startedAt,
                completedAt,
                durationMs,
                eventCount = ordered.Length,
                eventTypes = ordered
                    .GroupBy(static row => row.Event.EventType, StringComparer.Ordinal)
                    .OrderBy(static group => group.Key, StringComparer.Ordinal)
                    .Select(static group => new { eventType = group.Key, count = group.Count() }),
                timeline = ordered.Where(static row => IsTimelineEvent(row.Event.EventType)).Select(static row => new
                {
                    row.IngestId,
                    row.EdgeId,
                    row.Event.EventId,
                    row.Event.EventType,
                    row.Event.OccurredAt,
                    row.Event.Subject,
                    row.Event.Context,
                    row.Event.Data
                })
            }),
            Details =
            [
                new ResultDetailLink
                {
                    Kind = "cycle-query",
                    Label = "完整周期生产记录",
                    Url = cycleUrl
                }
            ],
            RelatedRecords = relatedRecords,
            Limitations = limitations,
            Outcome = ordered.Length > 0 && validDuration
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }

    private static bool IsTimelineEvent(string eventType)
        => eventType.EndsWith(".started", StringComparison.Ordinal) ||
           eventType.EndsWith(".completed", StringComparison.Ordinal) ||
           eventType.EndsWith(".cleared", StringComparison.Ordinal) ||
           eventType.EndsWith(".exited", StringComparison.Ordinal) ||
           eventType.StartsWith("alarm.", StringComparison.Ordinal) ||
           eventType.StartsWith("diagnostic.", StringComparison.Ordinal);
}
