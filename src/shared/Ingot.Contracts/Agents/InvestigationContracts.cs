namespace Ingot.Contracts.Agents;

public sealed record InvestigationTask
{
    public required string Question { get; init; }

    public required string Scope { get; init; }

    public int MaxRounds { get; init; } = 3;
}

public sealed record InvestigationHypothesis
{
    public required string HypothesisId { get; init; }

    public required string AuthorRole { get; init; }

    public required string Statement { get; init; }

    public required string Rationale { get; init; }

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];
}

public sealed record EvidenceClaim
{
    public required string HypothesisId { get; init; }

    public required string AuthorRole { get; init; }

    public required string Position { get; init; }

    public required string Statement { get; init; }

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];
}

public sealed record InvestigationContribution
{
    public required string Role { get; init; }

    public required int Round { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<InvestigationHypothesis> Hypotheses { get; init; } = [];

    public IReadOnlyList<EvidenceClaim> Claims { get; init; } = [];
}

public sealed record InvestigationVerdict
{
    public required string Status { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<InvestigationHypothesis> Hypotheses { get; init; } = [];

    public IReadOnlyList<EvidenceClaim> Claims { get; init; } = [];

    public IReadOnlyList<InvestigationContribution> Transcript { get; init; } = [];

    public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public static class InvestigationRoles
{
    public const string ProcessAnalyst = "process-analyst";
    public const string QualityAnalyst = "quality-analyst";
    public const string Skeptic = "skeptic";

    public static readonly IReadOnlyList<string> All =
        [ProcessAnalyst, QualityAnalyst, Skeptic];
}

public static class EvidenceClaimPositions
{
    public const string Support = "support";
    public const string Oppose = "oppose";
    public const string Uncertain = "uncertain";
}
