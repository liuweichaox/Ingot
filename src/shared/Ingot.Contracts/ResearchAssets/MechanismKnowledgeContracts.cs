namespace Ingot.Contracts.ResearchAssets;

public static class MechanismClaimStatuses
{
    public const string Draft = "draft";
    public const string Reviewed = "reviewed";
    public const string Supported = "supported";
    public const string Validated = "validated";
    public const string Active = "active";
    public const string Rejected = "rejected";
    public const string Falsified = "falsified";
    public const string Retired = "retired";

    public static bool IsValid(string? value)
        => value is Draft or Reviewed or Supported or Validated or Active or Rejected or Falsified or Retired;
}

public static class MechanismClaimTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "qualitative", "monotonic", "threshold", "interaction", "temporal",
        "constraint", "failure-mode", "executable-model"
    };
}

public sealed record MechanismClaimVariable
{
    public required string VariableCode { get; init; }
    public required string VariableRole { get; init; }
    public string? Direction { get; init; }
    public long? DelayMilliseconds { get; init; }
    public required string Unit { get; init; }
}

public sealed record MechanismClaimApplicability
{
    public required string DimensionCode { get; init; }
    public required string DimensionValue { get; init; }
}

public sealed record MechanismClaimConstraint
{
    public Guid ConstraintId { get; init; }
    public required string VariableCode { get; init; }
    public required string ConstraintKind { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public required string Unit { get; init; }
    public string Severity { get; init; } = "hard";
}

public sealed record MechanismClaimEvidence
{
    public Guid EvidenceLinkId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string ReferenceId { get; init; }
    public string Polarity { get; init; } = "supporting";
    public required string ContentHash { get; init; }
}

public sealed record MechanismClaimVersion
{
    public Guid ClaimId { get; init; }
    public Guid ProjectId { get; init; }
    public int Version { get; init; } = 1;
    public string Status { get; init; } = MechanismClaimStatuses.Draft;
    public required string Name { get; init; }
    public required string MechanismType { get; init; }
    public required string Statement { get; init; }
    public string? ExpectedSignature { get; init; }
    public required string FalsificationCondition { get; init; }
    public string EvidenceLevel { get; init; } = "engineering-observation";
    public IReadOnlyList<MechanismClaimVariable> Variables { get; init; } = [];
    public IReadOnlyList<MechanismClaimApplicability> Applicability { get; init; } = [];
    public IReadOnlyList<MechanismClaimConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<MechanismClaimEvidence> Evidence { get; init; } = [];
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public required string ContentHash { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record MechanismClaimReview
{
    public Guid ReviewId { get; init; }
    public Guid ClaimId { get; init; }
    public int ClaimVersion { get; init; }
    public required string Decision { get; init; }
    public string ReviewerId { get; init; } = "";
    public string? Comment { get; init; }
    public DateTimeOffset ReviewedAt { get; init; }
}

public sealed record MechanismClaimConflict
{
    public Guid ConflictId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid LeftClaimId { get; init; }
    public int LeftClaimVersion { get; init; }
    public Guid RightClaimId { get; init; }
    public int RightClaimVersion { get; init; }
    public required string ConflictKind { get; init; }
    public required string Rationale { get; init; }
    public string Status { get; init; } = "open";
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string? ResolvedBy { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public string? Resolution { get; init; }
}

public sealed record MechanismClaimReviewRequest(string Decision, string? Comment);

public sealed record MechanismClaimConflictRequest
{
    public Guid LeftClaimId { get; init; }
    public int LeftClaimVersion { get; init; } = 1;
    public Guid RightClaimId { get; init; }
    public int RightClaimVersion { get; init; } = 1;
    public required string ConflictKind { get; init; }
    public required string Rationale { get; init; }
}

public sealed record MechanismClaimConflictResolutionRequest
{
    public required string Resolution { get; init; }
}

public sealed record MechanismClaimUsage
{
    public Guid RecommendationId { get; init; }
    public Guid ClaimId { get; init; }
    public int ClaimVersion { get; init; }
    public required string UsageType { get; init; }
    public required string ContentHash { get; init; }
    public string? ClaimName { get; init; }
}

public sealed record MechanismClaimLifecycleRequest
{
    public required string TargetStatus { get; init; }
    public string? EvidenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public string? ContentHash { get; init; }
    public Guid? ValidationHypothesisId { get; init; }
    public string EvaluationOutcome { get; init; } = "supports";
    public string? EvaluationSummary { get; init; }
    public string? Comment { get; init; }
}

public sealed record MechanismClaimLifecycleDecision
{
    public Guid DecisionId { get; init; }
    public Guid ClaimId { get; init; }
    public int ClaimVersion { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public string? EvidenceKind { get; init; }
    public string? ReferenceId { get; init; }
    public string? ContentHash { get; init; }
    public Guid? ValidationHypothesisId { get; init; }
    public string? EvaluationOutcome { get; init; }
    public string? EvaluationSummary { get; init; }
    public string? Comment { get; init; }
    public required string DecidedBy { get; init; }
    public DateTimeOffset DecidedAt { get; init; }
}
