using System.Text.Json.Serialization;

namespace Ingot.Contracts.Agents;

public sealed record CreateChatRunRequest
{
    public required string Question { get; init; }

    public string? ConversationId { get; init; }

    public PageContextRef? PageContext { get; init; }

    public string Mode { get; init; } = "quick";

    [JsonIgnore]
    public IReadOnlyList<ChatConversationContextTurn> ConversationHistory { get; init; } = [];

    [JsonIgnore]
    public string? TriggerMessageId { get; init; }

    [JsonIgnore]
    public string? ResponseMessageId { get; init; }
}

public sealed record ChatConversationContextTurn
{
    public required string Question { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> Findings { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed record ChatCapabilities
{
    public required string EntryPoint { get; init; }

    public required string Purpose { get; init; }

    public required bool Enabled { get; init; }

    public required bool CombinedAnalysisEnabled { get; init; }

    public required string Provider { get; init; }

    public required string FastModel { get; init; }

    public required string ReasoningModel { get; init; }

    public required bool IsDeterministic { get; init; }

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

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];
}

public sealed record ChatRunSnapshot
{
    public required string RunId { get; init; }

    public required string ConversationId { get; init; }

    public required string UserId { get; init; }

    public required string EntryPoint { get; init; }

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

    public required string ConversationId { get; init; }

    public required string Question { get; init; }

    public PageContextRef? PageContext { get; init; }

    public required string EntryPoint { get; init; }

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

public sealed record ChatConversationSnapshot
{
    public required string ConversationId { get; init; }

    public required string Title { get; init; }

    public PageContextRef? PageContext { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyList<ChatRunSnapshot> Turns { get; init; } = [];
}
