// 实现只读 Agent 工具 GetProcessExecutionTraceTool，仅暴露授权范围内的确定性证据。

using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Application.Events;
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class GetProcessExecutionTraceTool(IChatEventReader events) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "get_execution_trace",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "按生产过程执行号还原一次完整生产过程。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "executionId" },
            properties = new { executionId = new { type = "string", minLength = 1, maxLength = 200 } },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        if (!call.Arguments.TryGetValue("executionId", out var executionId) ||
            string.IsNullOrWhiteSpace(executionId))
            throw new ArgumentException("请提供生产过程执行号。", nameof(call));
        executionId = executionId.Trim();
        var rows = await events.QueryAllAsync(
            context.UserId,
            new PlatformEventQuery { ExecutionId = executionId }, ct)
            .ConfigureAwait(false);
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId)
            .ToArray();
        var executionUrl = $"/api/v1/process-executions/{Uri.EscapeDataString(executionId)}";
        RelatedRecordRef[] relatedRecords =
        [
            new RelatedRecordRef
            {
                Kind = "process-execution-query",
                Id = $"correlation:{executionId}",
                Label = $"完整次执行 {executionId}",
                Url = executionUrl
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
            limitations.Add("当前范围没有生产记录，无法还原该生产过程执行。");
        if (ordered.Length > 0 && !startedAt.HasValue)
            limitations.Add("没有找到加工开始记录，无法确认过程执行起点和持续时间。");
        if (ordered.Length > 0 && !completedAt.HasValue)
            limitations.Add("没有找到加工完成记录，无法确认完整持续时间。");
        if (startedAt.HasValue && completedAt.HasValue && completedAt < startedAt)
            limitations.Add("过程执行完成时间早于开始时间，无法确认有效持续时间。");

        var summary = ordered.Length == 0
            ? $"没有找到生产过程执行 {executionId}。"
            : $"生产过程执行 {executionId} 包含 {ordered.Length} 条记录" +
              (startedAt.HasValue ? $"，开始于 {startedAt:O}" : "，未发现加工开始记录") +
              (completedAt.HasValue ? $"，完成于 {completedAt:O}。" : "，未发现加工完成记录。");
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new
            {
                executionId,
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
                    Kind = "process-execution-query",
                    Label = "完整次执行生产记录",
                    Url = executionUrl
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
