using System.Text.Json;

namespace Ingot.Contracts.Events;

public sealed record CycleSelectionComparisonRequest
{
    public required string BaselineCycleId { get; init; }

    public IReadOnlyList<string> CycleIds { get; init; } = [];
}

public sealed record CycleComparisonResult
{
    public required string BaselineCycleId { get; init; }

    public required string ProductSeries { get; init; }

    public string? AnalysisPlanId { get; init; }

    public int? AnalysisPlanVersion { get; init; }

    public string? DataModelId { get; init; }

    public int? DataModelVersion { get; init; }

    public string AnalysisScope { get; init; } = "production-cycle";

    public string? AlignmentMode { get; init; }

    public string FeatureAlgorithmVersion { get; init; } = "stage-relative-v1";

    public string EvidenceLevel { get; init; } = "insufficient";

    public required CycleComparisonRow Baseline { get; init; }

    public IReadOnlyList<CycleComparisonRow> HistoricalCycles { get; init; } = [];

    public IReadOnlyList<CycleSignalComparison> SignalComparisons { get; init; } = [];

    public IReadOnlyList<CycleQualityAssociation> QualityAssociations { get; init; } = [];

    public required CycleComparisonAcceptance Acceptance { get; init; }
}

public sealed record CycleSignalComparison
{
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public double? BaselineValue { get; init; }
    public double? HistoricalMedian { get; init; }
    public double? HistoricalP10 { get; init; }
    public double? HistoricalP90 { get; init; }
    public double? BaselinePercentile { get; init; }
    public double? RobustDeviation { get; init; }
    public double EffectiveWeight { get; init; }
}

public sealed record CycleQualityAssociation
{
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public int PassCycleCount { get; init; }
    public int FailCycleCount { get; init; }
    public double PassEffectiveWeight { get; init; }
    public double FailEffectiveWeight { get; init; }
    public double? PassMedian { get; init; }
    public double? FailMedian { get; init; }
    public double? MedianDifference { get; init; }
    public double? RobustEffect { get; init; }
    public double CandidateScore { get; init; }
    public string EvidenceLevel { get; init; } = "insufficient";
    public IReadOnlyList<string> PossibleConfounders { get; init; } = [];
}

public sealed record CycleComparisonRow
{
    public required string CorrelationId { get; init; }

    public required string MachineId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public double? DurationMs { get; init; }

    public required string ProductSeries { get; init; }

    public string? ProductCode { get; init; }

    public string? RecipeId { get; init; }

    public string? RecipeVersion { get; init; }

    public string? ToolingInstallationId { get; init; }

    public string? ToolingId { get; init; }

    public string? MoldId { get; init; }

    public string? AssemblyRevisionId { get; init; }

    public string? AssemblyRevision { get; init; }

    public int SampleCount { get; init; }

    public int ExpectedSampleCount { get; init; }

    /// <summary>旧接口兼容字段；新分析请使用 ProcessDataQuality。</summary>
    public double? SampleCompleteness { get; init; }

    public ProcessDataQualitySummary ProcessDataQuality { get; init; } = new();

    public double EvidenceWeight { get; init; }

    public int PhaseCount { get; init; }

    public int RequiredPhaseCount { get; init; }

    public bool PhaseComplete { get; init; }

    public IReadOnlyList<string> InspectionOutcomes { get; init; } = [];

    public string? VisualReviewDecision { get; init; }

    public IReadOnlyList<CycleSignalStatistic> Signals { get; init; } = [];

    public IReadOnlyList<CyclePhaseSummary> Phases { get; init; } = [];

    public CycleAnalysisMaterialization AnalysisMaterialization { get; init; } = new();

    public IReadOnlyList<CycleRecipeParameter> RecipeParameters { get; init; } = [];
}

public sealed record CycleRecipeParameter
{
    public required string Code { get; init; }

    public string? Name { get; init; }

    public string? Unit { get; init; }

    public JsonElement Value { get; init; }
}

public sealed record CycleSignalStatistic
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Unit { get; init; }

    public int SampleCount { get; init; }

    public double? Average { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double ValidDurationMs { get; init; }

    public double Coverage { get; init; }

    public IReadOnlyList<CycleSignalFeature> Features { get; init; } = [];
}

public sealed record CycleComparisonAcceptance
{
    public int CycleCount { get; init; }

    public int CompleteCycleCount { get; init; }

    public int PhaseCompleteCycleCount { get; init; }

    public int QualityLinkedCycleCount { get; init; }

    public int VisualReviewCompletedCycleCount { get; init; }

    public int AvailableCycleCount { get; init; }

    public int DegradedCycleCount { get; init; }

    public int UnavailableCycleCount { get; init; }

    public double EffectiveCycleWeight { get; init; }
}
