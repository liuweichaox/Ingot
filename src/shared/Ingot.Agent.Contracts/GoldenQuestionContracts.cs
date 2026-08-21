using System.Text.Json;

namespace Ingot.Contracts.Agents;

public static class GoldenQuestionStatuses
{
    public const string Draft = "draft";
    public const string Reviewed = "reviewed";
    public const string Retired = "retired";

    public static bool IsValid(string value) => value is Draft or Reviewed or Retired;
}

public sealed record GoldenExpectedFact
{
    public required string FactId { get; init; }
    public required string Tool { get; init; }

    public required string JsonPointer { get; init; }
    public required JsonElement ExpectedValue { get; init; }

    public string? AnswerMustContain { get; init; }
}

public sealed record GoldenQuestionCase
{
    public Guid CaseId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public required string Question { get; init; }
    public string EntryPoint { get; init; } = ProductEntryPoints.Chat;
    public string Mode { get; init; } = "quick";
    public PageContextRef? PageContext { get; init; }
    public IReadOnlyList<GoldenExpectedFact> ExpectedFacts { get; init; } = [];
    public IReadOnlyList<RelatedRecordRef> ExpectedRecordReferences { get; init; } = [];
    public bool ExpectRefusal { get; init; }
    public IReadOnlyList<string> ForbiddenAnswerText { get; init; } = [];
    public string Status { get; init; } = GoldenQuestionStatuses.Draft;
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record GoldenEvaluationGate
{
    public required string Code { get; init; }
    public required bool Passed { get; init; }
    public required string Detail { get; init; }
}

public sealed record GoldenQuestionEvaluation
{
    public Guid EvaluationId { get; init; }
    public Guid CaseId { get; init; }
    public int CaseVersion { get; init; }
    public required string AgentRunId { get; init; }
    public required bool Passed { get; init; }
    public IReadOnlyList<GoldenEvaluationGate> Gates { get; init; } = [];
    public required string ModelProvider { get; init; }
    public required string Model { get; init; }
    public required string PromptVersion { get; init; }
    public required string ToolsetVersion { get; init; }

    public string? AgentRunSnapshotHash { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; }
}

public sealed record GoldenEvaluationRequest
{
    public required string AgentRunId { get; init; }
}

public sealed record GoldenEvaluationSummary
{
    public int EvaluationCount { get; init; }
    public int PassedCount { get; init; }
    public double PassRate { get; init; }
    public double FactGatePassRate { get; init; }
    public double ReferenceGatePassRate { get; init; }
    public double RefusalGatePassRate { get; init; }
    public double CausalGuardGatePassRate { get; init; }
}
