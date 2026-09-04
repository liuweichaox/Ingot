using System.Text.Json;

namespace Ingot.Contracts.Agents;

public sealed record AgentStreamEvent
{
    public required long Sequence { get; init; }

    public required string Type { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public JsonElement? Data { get; init; }
}

public static class AgentRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Cancelling = "cancelling";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status)
        => status is Completed or Failed or Cancelled;
}

public static class AgentStreamEventTypes
{
    public const string RunStarted = "run.started";
    public const string PlanCreated = "plan.created";
    public const string PlanRejected = "plan.rejected";
    public const string IterationStarted = "iteration.started";
    public const string IterationCompleted = "iteration.completed";
    public const string ToolStarted = "tool.started";
    public const string ToolCompleted = "tool.completed";
    public const string ToolFailed = "tool.failed";
    public const string RelatedRecordsChecked = "relatedRecords.checked";
    public const string AnswerDelta = "answer.delta";
    public const string ChartCompleted = "chart.completed";
    public const string DiscussionStarted = "discussion.started";
    public const string DiscussionMessage = "discussion.message";
    public const string DiscussionParticipantFailed = "discussion.participant_failed";
    public const string DiscussionCompleted = "discussion.completed";
    public const string RunCompleted = "run.completed";
    public const string RunFailed = "run.failed";
    public const string RunCancelled = "run.cancelled";
}
