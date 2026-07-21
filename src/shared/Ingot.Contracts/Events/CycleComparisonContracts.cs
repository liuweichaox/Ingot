namespace Ingot.Contracts.Events;

public sealed record CycleComparisonResult
{
    public required string BaselineCycleId { get; init; }

    public required string ProductSeries { get; init; }

    public required CycleComparisonRow Baseline { get; init; }

    public IReadOnlyList<CycleComparisonRow> HistoricalCycles { get; init; } = [];

    public required CycleComparisonAcceptance Acceptance { get; init; }
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

    public int SampleCount { get; init; }

    public int ExpectedSampleCount { get; init; }

    public double SampleCompleteness { get; init; }

    public int PhaseCount { get; init; }

    public int RequiredPhaseCount { get; init; }

    public bool PhaseComplete { get; init; }

    public IReadOnlyList<string> InspectionOutcomes { get; init; } = [];

    public string? VisualReviewDecision { get; init; }

    public IReadOnlyList<CycleSignalStatistic> Signals { get; init; } = [];
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
}

public sealed record CycleComparisonAcceptance
{
    public int CycleCount { get; init; }

    public int CompleteCycleCount { get; init; }

    public int PhaseCompleteCycleCount { get; init; }

    public int QualityLinkedCycleCount { get; init; }

    public int VisualReviewCompletedCycleCount { get; init; }
}
