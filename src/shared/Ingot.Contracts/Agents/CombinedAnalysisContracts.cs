namespace Ingot.Contracts.Agents;

public sealed record CombinedAnalysisTask
{
    public required string Question { get; init; }

    public required string Scope { get; init; }

    public int MaxRounds { get; init; } = 3;
}

public sealed record PossibleCause
{
    public required string CauseId { get; init; }

    public required string AuthorRole { get; init; }

    public required string Statement { get; init; }

    public required string Reason { get; init; }

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];
}

public sealed record FindingReview
{
    public required string CauseId { get; init; }

    public required string AuthorRole { get; init; }

    public required string Position { get; init; }

    public required string Statement { get; init; }

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];
}

public sealed record PerspectiveAnalysis
{
    public required string Role { get; init; }

    public required int Round { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<PossibleCause> PossibleCauses { get; init; } = [];

    public IReadOnlyList<FindingReview> Reviews { get; init; } = [];
}

public sealed record CombinedAnalysisResult
{
    public required string Status { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<PossibleCause> PossibleCauses { get; init; } = [];

    public IReadOnlyList<FindingReview> Reviews { get; init; } = [];

    public IReadOnlyList<PerspectiveAnalysis> ReviewSteps { get; init; } = [];

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public static class AnalysisPerspectives
{
    public const string Process = "process";
    public const string Quality = "quality";
    public const string Review = "review";

    public static readonly IReadOnlyList<string> All =
        [Process, Quality, Review];
}

public static class FindingReviewPositions
{
    public const string Support = "support";
    public const string Oppose = "oppose";
    public const string Uncertain = "uncertain";
}
