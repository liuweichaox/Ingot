namespace Ingot.Platform.Application.ResearchAssets;

public sealed record KnowledgeExtractionJob(
    Guid SourceId,
    string UserId,
    Guid LeaseId,
    int AttemptCount);

public enum KnowledgeExtractionFailureDisposition
{
    RetryScheduled,
    DeadLettered
}

public sealed record MechanismClaimDraftGenerationRequest
{
    public Guid SourceId { get; init; }
    public string? Focus { get; init; }
}

public sealed record MechanismClaimDraftGenerationContext
{
    public required string ProjectName { get; init; }
    public IReadOnlyDictionary<string, string> ProjectContext { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<MechanismDraftVariable> Variables { get; init; } = [];
    public required string SourceTitle { get; init; }
    public required string SourceHash { get; init; }
    public IReadOnlyList<MechanismDraftFragment> Fragments { get; init; } = [];
    public string? Focus { get; init; }
}

public sealed record MechanismDraftVariable(string Code, string Role, string Unit);
public sealed record MechanismDraftFragment(Guid RecordId, string Content, string ContentHash);

public sealed record GeneratedMechanismClaimDraft
{
    public required string Name { get; init; }
    public required string MechanismType { get; init; }
    public required string Statement { get; init; }
    public string? ExpectedSignature { get; init; }
    public required string FalsificationCondition { get; init; }
    public IReadOnlyList<Ingot.Contracts.ResearchAssets.MechanismClaimVariable> Variables { get; init; } = [];
    public IReadOnlyList<Ingot.Contracts.ResearchAssets.MechanismClaimApplicability> Applicability { get; init; } = [];
    public IReadOnlyList<Ingot.Contracts.ResearchAssets.MechanismClaimConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<Ingot.Contracts.ResearchAssets.MechanismForbiddenCombination> ForbiddenCombinations { get; init; } = [];
    public IReadOnlyList<Guid> SupportingRecordIds { get; init; } = [];
    public string GeneratorModel { get; init; } = "";
}

/// <summary>根据受控来源生成待人工复核的结构化机理声明草稿。</summary>
public interface IMechanismClaimDraftGenerator
{
    Task<GeneratedMechanismClaimDraft> GenerateAsync(
        MechanismClaimDraftGenerationContext context,
        CancellationToken ct = default);
}
