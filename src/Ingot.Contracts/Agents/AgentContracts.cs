using System.Text.Json;

namespace Ingot.Contracts.Agents;

public sealed record CreateChatRunRequest
{
    public required string Question { get; init; }

    public PageContextRef? PageContext { get; init; }

    public string Mode { get; init; } = "standard";
}

public static class ProductSurfaces
{
    public const string Chat = "chat";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Chat
    };
}

public static class RunPurposes
{
    public const string ReadOnlyAnalysis = "read-only-analysis";

    public static string ForSurface(string surface) => surface switch
    {
        ProductSurfaces.Chat => ReadOnlyAnalysis,
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "不支持的产品面。")
    };
}

public sealed record PageContextRef
{
    public required string Kind { get; init; }

    public required string Id { get; init; }
}

public sealed record AnalysisPlan
{
    public string Surface { get; init; } = string.Empty;

    public required string Intent { get; init; }

    public required string Summary { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyList<AnalysisToolCall> ToolCalls { get; init; } = [];
}

public sealed record AnalysisToolCall
{
    public required string Tool { get; init; }

    public IReadOnlyDictionary<string, string?> Arguments { get; init; }
        = new Dictionary<string, string?>();
}

public sealed record AnalysisAnswer
{
    public required string Summary { get; init; }

    public IReadOnlyList<string> Findings { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];

    public IReadOnlyList<ChartSpec> Charts { get; init; } = [];

    public IReadOnlyList<string> FollowUpQuestions { get; init; } = [];

    public InvestigationVerdict? Investigation { get; init; }
}

public sealed record EvidenceRef
{
    public required string Kind { get; init; }

    public required string Id { get; init; }

    public required string Label { get; init; }

    public string? Url { get; init; }
}

public sealed record ChartSpec
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<ChartSeries> Series { get; init; } = [];
}

public sealed record ChartSeries
{
    public required string Name { get; init; }

    public IReadOnlyList<double?> Values { get; init; } = [];
}

public sealed record AgentToolInvocation
{
    public required string Tool { get; init; }

    public required string Version { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Summary { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];
}

public sealed record AgentRunSnapshot
{
    public required string RunId { get; init; }

    public required string ActorId { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required string Question { get; init; }

    public PageContextRef? PageContext { get; init; }

    public required string Mode { get; init; }

    public required string Status { get; init; }

    public required string ModelProvider { get; init; }

    public required string Model { get; init; }

    public required string PromptVersion { get; init; }

    public required string ToolsetVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public AnalysisPlan? Plan { get; init; }

    public string WorkflowStage { get; init; } = "analysis";

    public int Iteration { get; init; }

    public IReadOnlyList<AgentToolInvocation> ToolInvocations { get; init; } = [];

    public AnalysisAnswer? Answer { get; init; }

    public required AgentUsageSummary Usage { get; init; }

    public string? Error { get; init; }

    public string? CancellationReason { get; init; }
}

public sealed record AgentUsageSummary
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }

    public int ModelCalls { get; init; }

    public int ToolCalls { get; init; }

    public decimal? EstimatedCost { get; init; }

    public string Currency { get; init; } = "USD";
}

public sealed record AgentCapabilities
{
    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required bool Enabled { get; init; }

    public required bool DeepInvestigationEnabled { get; init; }

    public required string Provider { get; init; }

    public required string FastModel { get; init; }

    public required string ReasoningModel { get; init; }

    public IReadOnlyList<string> Modes { get; init; } = [];

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<AgentToolCapability> Tools { get; init; } = [];

    public required int MaxToolCalls { get; init; }

    public required int MaxRunSeconds { get; init; }

    public required int MaxDiscussionRounds { get; init; }

    public required int MaxDiscussionTurns { get; init; }
}

public sealed record ChatCapabilities
{
    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required bool Enabled { get; init; }

    public required bool DeepInvestigationEnabled { get; init; }

    public required string Provider { get; init; }

    public required string FastModel { get; init; }

    public required string ReasoningModel { get; init; }

    public IReadOnlyList<string> Modes { get; init; } = [];

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<ChatToolCapability> Tools { get; init; } = [];

    public required int MaxToolCalls { get; init; }

    public required int MaxRunSeconds { get; init; }

    public required int MaxDiscussionRounds { get; init; }

    public required int MaxDiscussionTurns { get; init; }
}

public sealed record ChatToolCapability
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required string Description { get; init; }

    public string Access { get; init; } = "read";
}

public sealed record ChatToolInvocation
{
    public required string Tool { get; init; }

    public required string Version { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Summary { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];
}

public sealed record ChatRunSnapshot
{
    public required string RunId { get; init; }

    public required string ActorId { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required string Question { get; init; }

    public PageContextRef? PageContext { get; init; }

    public required string Mode { get; init; }

    public required string Status { get; init; }

    public required string ModelProvider { get; init; }

    public required string Model { get; init; }

    public required string PromptVersion { get; init; }

    public required string ToolsetVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public AnalysisPlan? Plan { get; init; }

    public string WorkflowStage { get; init; } = "analysis";

    public int Iteration { get; init; }

    public IReadOnlyList<ChatToolInvocation> ToolInvocations { get; init; } = [];

    public AnalysisAnswer? Answer { get; init; }

    public required AgentUsageSummary Usage { get; init; }

    public string? Error { get; init; }

    public string? CancellationReason { get; init; }
}

public sealed record ChatRunListItem
{
    public required string RunId { get; init; }

    public required string Question { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required string Mode { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Summary { get; init; }

    public AgentUsageSummary Usage { get; init; } = new();
}

public sealed record ChatRunPage
{
    public IReadOnlyList<ChatRunListItem> Items { get; init; } = [];

    public DateTimeOffset? NextBefore { get; init; }
}

public sealed record AgentToolCapability
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required string Description { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public string Access { get; init; } = "read";
}

public sealed record AgentRunListItem
{
    public required string RunId { get; init; }

    public required string Question { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required string Mode { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? Summary { get; init; }

    public AgentUsageSummary Usage { get; init; } = new();
}

public sealed record AgentRunPage
{
    public IReadOnlyList<AgentRunListItem> Items { get; init; } = [];

    public DateTimeOffset? NextBefore { get; init; }
}

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
    public const string EvidenceVerified = "evidence.verified";
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
