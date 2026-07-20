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
        var rows = await events.QueryAsync(
            context.UserId,
            new PlatformEventQuery { CorrelationId = correlationId, Limit = 500 }, ct)
            .ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId)
            .ToArray();
        var relatedRecords = ordered.Select(row => new RelatedRecordRef
        {
            Kind = "production-event",
            Id = row.Event.EventId,
            Label = $"{row.Event.EventType} · {row.Event.OccurredAt:O}",
            Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(correlationId)}&limit=500"
        }).ToArray();
        if (ordered.Length == 0)
        {
            relatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "event-query",
                    Id = $"correlation:{correlationId}",
                    Label = $"周期查询 {correlationId}",
                    Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(correlationId)}&limit=500"
                }
            ];
        }

        var startedAt = ordered.FirstOrDefault(row =>
            row.Event.EventType.EndsWith(".started", StringComparison.Ordinal))?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(row =>
            row.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
            row.Event.EventType.EndsWith(".exited", StringComparison.Ordinal))?.Event.OccurredAt;
        var reachedResultLimit = ordered.Length == 500;
        var durationMs = startedAt is { } start && completedAt is { } completed && completed >= start
            ? (completed - start).TotalMilliseconds
            : (double?)null;
        var validDuration = durationMs.HasValue;
        var limitations = new List<string>();
        if (ordered.Length == 0)
            limitations.Add("当前范围没有生产记录，无法还原该生产周期。");
        if (reachedResultLimit)
            limitations.Add("该周期超过 500 条生产记录，当前结果可能不完整。");
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
                events = ordered.Select(static row => new
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
            RelatedRecords = relatedRecords,
            Limitations = limitations,
            Outcome = ordered.Length > 0 && !reachedResultLimit && validDuration
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }
}
