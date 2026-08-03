namespace Ingot.Contracts.Analytics;

public sealed record DataReliabilityBaselineQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? EdgeId { get; init; }
    public string? EquipmentId { get; init; }
    public int MaximumRuns { get; init; } = 2000;
}

public sealed record ReliabilityRate
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int Numerator { get; init; }
    public int Denominator { get; init; }
    public double? Rate { get; init; }
    public required string Definition { get; init; }
}

public sealed record ContextFieldCoverage
{
    public required string Field { get; init; }
    public int PresentRunCount { get; init; }
    public int RunCount { get; init; }
    public double? Coverage { get; init; }
    public bool RequiredForAdmission { get; init; }
}

public sealed record ReliabilityExclusionCount
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int RunCount { get; init; }
}

public sealed record DataReliabilityBaseline
{
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? EdgeId { get; init; }
    public string? EquipmentId { get; init; }
    public int MatchingCompletedRunCount { get; init; }
    public int AnalyzedRunCount { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<ReliabilityRate> Rates { get; init; } = [];
    public IReadOnlyList<ContextFieldCoverage> ContextFields { get; init; } = [];
    public IReadOnlyList<ReliabilityExclusionCount> Exclusions { get; init; } = [];
    public int DuplicateTimestampCount { get; init; }
    public int OutOfOrderCount { get; init; }
    public int SequenceGapCount { get; init; }
    public double? MaximumSampleGapMs { get; init; }
}
