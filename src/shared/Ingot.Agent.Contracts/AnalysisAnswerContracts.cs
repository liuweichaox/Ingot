namespace Ingot.Contracts.Agents;

public sealed record AnalysisPlan
{
    public string EntryPoint { get; init; } = string.Empty;

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

    public string SummaryStrength { get; init; } = AnalysisClaimStrengths.Observation;

    public IReadOnlyList<AnalysisClaim> Findings { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];

    public IReadOnlyList<ChartSpec> Charts { get; init; } = [];

    public IReadOnlyList<string> FollowUpQuestions { get; init; } = [];

    public IReadOnlyList<AgentProposalEnvelope> Proposals { get; init; } = [];

    public CombinedAnalysisResult? CombinedAnalysis { get; init; }
}

public static class AnalysisClaimStrengths
{
    public const string Observation = "observation";

    public const string Association = "association";

    public const string Hypothesis = "hypothesis";

    public const string Causal = "causal";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Observation,
        Association,
        Hypothesis,
        Causal
    };
}

public sealed record AnalysisClaim
{
    public required string Statement { get; init; }

    public string Strength { get; init; } = AnalysisClaimStrengths.Observation;

    public IReadOnlyList<RelatedRecordRef> EvidenceReferences { get; init; } = [];
}

public static class AgentProposalKinds
{
    public const string Investigation = "investigation";
    public const string Hypothesis = "hypothesis";
    public const string RecipeRecommendation = "recipe-recommendation";
    public const string ProductionEvidence = "production-evidence";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Investigation,
        Hypothesis,
        RecipeRecommendation,
        ProductionEvidence
    };
}

public sealed record AgentProposalEnvelope
{
    public required string Kind { get; init; }

    public required string Title { get; init; }

    public required string Rationale { get; init; }

    public IReadOnlyDictionary<string, string> DraftFields { get; init; }
        = new Dictionary<string, string>();

    public IReadOnlyList<RelatedRecordRef> EvidenceReferences { get; init; } = [];

    public string Persistence { get; init; } = "preview-only";

    public bool RequiresHumanConfirmation { get; init; } = true;
}

public sealed record RelatedRecordRef
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
