// 定义数据可靠性基线的站点范围、统计结果和排除原因。
namespace Ingot.Contracts.Analytics;

public sealed record DataReliabilityBaselineQuery
{
    public string? SiteId { get; init; }
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

public sealed record ContextFactorLevelSummary
{
    public required string Value { get; init; }
    public int RunCount { get; init; }
    public int ProcessCompleteRunCount { get; init; }
    public int QualityLinkedRunCount { get; init; }
    public int PassRunCount { get; init; }
    public int FailRunCount { get; init; }
    public int InconclusiveRunCount { get; init; }
    public double? MeanDurationMs { get; init; }
}

public sealed record ContextFactorSummary
{
    public required string Field { get; init; }
    public required string Name { get; init; }
    public int PresentRunCount { get; init; }
    public int MissingRunCount { get; init; }
    public int DistinctLevelCount { get; init; }
    public double? Coverage { get; init; }
    public bool LevelsTruncated { get; init; }
    public IReadOnlyList<ContextFactorLevelSummary> Levels { get; init; } = [];
}

public sealed record ContextFactorOverlap
{
    public required string LeftField { get; init; }
    public required string RightField { get; init; }
    public int JointRunCount { get; init; }
    public int LeftLevelCount { get; init; }
    public int RightLevelCount { get; init; }
    public int ObservedCombinationCount { get; init; }
    public int PossibleCombinationCount { get; init; }
    public double? OverlapRate { get; init; }

    public required string Identifiability { get; init; }
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
    public IReadOnlyList<ContextFactorSummary> ContextFactors { get; init; } = [];
    public IReadOnlyList<ContextFactorOverlap> ContextFactorOverlaps { get; init; } = [];
    public int UnidentifiableConfoundingCount { get; init; }
    public IReadOnlyList<ReliabilityExclusionCount> Exclusions { get; init; } = [];
    public int DuplicateTimestampCount { get; init; }
    public int OutOfOrderCount { get; init; }
    public int SequenceGapCount { get; init; }
    public double? MaximumSampleGapMs { get; init; }
    public double? MaximumAbsoluteSourceClockOffsetMs { get; init; }

    public double? WorstRunP95PlatformIngestLatencyMs { get; init; }
    public double? MaximumPlatformIngestLatencyMs { get; init; }
    public int NegativePlatformIngestLatencyCount { get; init; }
}
