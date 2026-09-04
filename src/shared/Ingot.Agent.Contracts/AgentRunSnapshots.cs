using System.Text.Json;

namespace Ingot.Contracts.Agents;

public sealed record AgentToolInvocation
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

public sealed record AgentToolResultSnapshot
{
    public required string Tool { get; init; }

    public required string Version { get; init; }

    public required string Summary { get; init; }

    public required JsonElement Data { get; init; }

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];

    public required string Outcome { get; init; }

    public required string ContentHash { get; init; }

    public required DateTimeOffset VerifiedAt { get; init; }
}

public sealed record AgentRunSnapshot
{
    public required string RunId { get; init; }

    public string? ConversationId { get; init; }

    public string? TriggerMessageId { get; init; }

    public string? ResponseMessageId { get; init; }

    public required string UserId { get; init; }

    public required string EntryPoint { get; init; }

    public required string Purpose { get; init; }

    public required string Question { get; init; }

    public PageContextRef? PageContext { get; init; }

    /// <summary>
    /// Server-derived authorization scope captured when the run is admitted. Null denotes a
    /// legacy snapshot whose data scope cannot be proven and must therefore be treated as denied.
    /// </summary>
    public AgentRunAccessScopeSnapshot? AccessScope { get; init; }

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

    public IReadOnlyList<AgentToolResultSnapshot> ToolResults { get; init; } = [];

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

public sealed record AgentRunListItem
{
    public required string RunId { get; init; }

    public required string ConversationId { get; init; }

    public required string UserId { get; init; }

    public required string Question { get; init; }

    public PageContextRef? PageContext { get; init; }

    public AgentRunAccessScopeSnapshot? AccessScope { get; init; }

    public required string EntryPoint { get; init; }

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
