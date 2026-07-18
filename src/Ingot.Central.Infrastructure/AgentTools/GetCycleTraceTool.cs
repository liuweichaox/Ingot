using System.Text.Json;
using Ingot.Agent;
using Ingot.Central.Infrastructure.Events;
using Ingot.Contracts.Agents;

namespace Ingot.Central.Infrastructure.AgentTools;

public sealed class GetCycleTraceTool(IChatEventReader events) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "get_cycle_trace",
        Version = "1.0.0",
        Surface = ProductSurfaces.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "按 CorrelationId 返回一个生产周期的不可变事件时间线。只读。",
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
            throw new ArgumentException("get_cycle_trace 需要 correlationId。 ", nameof(call));
        correlationId = correlationId.Trim();
        var rows = await events.QueryAsync(
            context.ActorId,
            new CentralEventQuery { CorrelationId = correlationId, Limit = 500 }, ct)
            .ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId)
            .ToArray();
        var evidence = ordered.Select(row => new EvidenceRef
        {
            Kind = "production-event",
            Id = row.Event.EventId,
            Label = $"{row.Event.EventType} · {row.Event.OccurredAt:O}",
            Url = $"/api/v1/events?correlationId={Uri.EscapeDataString(correlationId)}&limit=500"
        }).ToArray();
        if (ordered.Length == 0)
        {
            evidence =
            [
                new EvidenceRef
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
            limitations.Add("当前范围没有生产事件，无法还原周期事实链。");
        if (reachedResultLimit)
            limitations.Add("周期查询达到 500 条上限，无法确认事实链完整性。");
        if (ordered.Length > 0 && !startedAt.HasValue)
            limitations.Add("没有找到周期开始事件，无法确认周期起点和持续时间。");
        if (ordered.Length > 0 && !completedAt.HasValue)
            limitations.Add("没有找到周期完成事件，无法确认完整持续时间。");
        if (startedAt.HasValue && completedAt.HasValue && completedAt < startedAt)
            limitations.Add("周期完成时间早于开始时间，无法确认有效持续时间。");

        var summary = ordered.Length == 0
            ? $"没有找到关联 ID 为 {correlationId} 的生产周期。"
            : $"周期 {correlationId} 包含 {ordered.Length} 条事件" +
              (startedAt.HasValue ? $"，开始于 {startedAt:O}" : "，未发现开始事件") +
              (completedAt.HasValue ? $"，完成于 {completedAt:O}。" : "，未发现完成事件。");
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
            Evidence = evidence,
            Limitations = limitations,
            Outcome = ordered.Length > 0 && !reachedResultLimit && validDuration
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }
}
