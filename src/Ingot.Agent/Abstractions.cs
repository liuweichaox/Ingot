using System.Text.Json;
using Ingot.Contracts.Agents;

namespace Ingot.Agent;

public interface IAgentRuntime
{
    AgentCapabilities GetCapabilities(string surface);

    Task<AgentRunPage> ListAsync(
        string surface,
        string actorId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default);

    Task<AgentRunSnapshot> StartAsync(
        string surface,
        string actorId,
        CreateChatRunRequest request,
        CancellationToken ct = default);

    Task<AgentRunSnapshot?> GetAsync(string surface, string runId, CancellationToken ct = default);

    IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        string surface,
        string runId,
        long afterSequence = 0,
        CancellationToken ct = default);

    Task<bool> CancelAsync(
        string surface,
        string runId,
        string actorId,
        string reason,
        CancellationToken ct = default);
}

public interface IAgentRunStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default);

    Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
        string surface,
        string actorId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default);

    Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default);

    Task<AgentStreamEvent> AppendEventAsync(
        string runId,
        string type,
        object? data,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
        string runId,
        long afterSequence,
        int limit,
        CancellationToken ct = default);
}

public interface IModelRouter
{
    IModelClient GetClient(string surface, ModelRole role);
}

public enum ModelRole
{
    Fast,
    Reasoning
}

public interface IModelClient
{
    string Surface => "*";

    string Provider { get; }

    string Model { get; }

    Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
        CreateChatRunRequest request,
        IReadOnlyCollection<AnalysisToolDefinition> tools,
        CancellationToken ct = default);

    Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        CancellationToken ct = default);

    Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(
        InvestigationTurn turn,
        CancellationToken ct = default);
}

public sealed record InvestigationTurn
{
    public required string Role { get; init; }

    public required int Round { get; init; }

    public required InvestigationTask Task { get; init; }

    public required CreateChatRunRequest Request { get; init; }

    public required AnalysisPlan Plan { get; init; }

    public required IReadOnlyList<AnalysisToolResult> ToolResults { get; init; }

    public IReadOnlyList<InvestigationHypothesis> Hypotheses { get; init; } = [];

    public IReadOnlyList<EvidenceClaim> Claims { get; init; } = [];
}

public sealed record ModelCallUsage
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Operation { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long DurationMilliseconds { get; init; }
}

public sealed record ModelCallResult<T>
{
    public required T Value { get; init; }

    public required ModelCallUsage Usage { get; init; }
}

public sealed record InvestigationWorkflowResult
{
    public required InvestigationVerdict Verdict { get; init; }

    public IReadOnlyList<ModelCallUsage> ModelCalls { get; init; } = [];
}

public interface IInvestigationWorkflow
{
    Task<InvestigationWorkflowResult> RunAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        IModelClient model,
        Func<string, object?, CancellationToken, Task> publish,
        CancellationToken ct = default);
}

public interface IAnalysisTool
{
    AnalysisToolDefinition Definition { get; }

    Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default);
}

public sealed record AnalysisToolDefinition
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required string Description { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required JsonElement InputSchema { get; init; }

    public string Access { get; init; } = AgentToolAccess.Read;
}

public static class AgentToolAccess
{
    public const string Read = "read";
}

public sealed record AnalysisToolResult
{
    public required string Tool { get; init; }

    public required string Summary { get; init; }

    public required JsonElement Data { get; init; }

    public IReadOnlyList<AnalysisArtifactRef> Artifacts { get; init; } = [];

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];

    public string Outcome { get; init; } = AnalysisToolOutcomes.Sufficient;
}

public sealed record AnalysisArtifactRef
{
    public required string Kind { get; init; }

    public required string Label { get; init; }

    public required string Url { get; init; }

    public long? SizeBytes { get; init; }
}

public static class AnalysisToolOutcomes
{
    public const string Sufficient = "sufficient";

    public const string InsufficientData = "insufficient-data";
}

public sealed record AgentExecutionContext
{
    public required string RunId { get; init; }

    public required string ActorId { get; init; }

    public required string Surface { get; init; }

    public required string Purpose { get; init; }

    public required CreateChatRunRequest Request { get; init; }
}

public interface IPlanValidator
{
    bool TryValidate(
        string surface,
        AnalysisPlan plan,
        IReadOnlyDictionary<string, IAnalysisTool> tools,
        out string error);
}

public interface IEvidenceVerifier
{
    bool TryVerify(
        IReadOnlyList<AnalysisToolResult> results,
        out IReadOnlyList<EvidenceRef> evidence,
        out string error);

    bool TryVerifyAnswer(
        AnalysisAnswer answer,
        IReadOnlyList<AnalysisToolResult> results,
        out string error);
}
