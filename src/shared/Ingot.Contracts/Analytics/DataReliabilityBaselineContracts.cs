// 定义数据可靠性基线的站点范围、统计结果和排除原因。
using Ingot.Contracts.Events;

namespace Ingot.Contracts.Analytics;

public sealed record DataReliabilityBaselineQuery
{
    public string? SiteId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? EdgeId { get; init; }
    public string? EquipmentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
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

public sealed record ReliabilityRunImpact
{
    public required string ExecutionId { get; init; }
    public required string EquipmentId { get; init; }
    public IReadOnlyList<string> EdgeIds { get; init; } = [];
    public bool HasStarted { get; init; }
    public bool HasCompleted { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required string ProcessDataStatus { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
    public IReadOnlyList<ProcessSampleConfigurationEvidence> SampleConfigurationEvidence { get; init; } = [];
    public IReadOnlyList<ExpectedConfigurationCoverageEvidence> ExpectedConfigurationCoverage { get; init; } = [];
}

/// <summary>Observed applied-version and mapping evidence overlapping one run; overlap is not causal proof.</summary>
public sealed record ExpectedConfigurationCoverageEvidence
{
    public required string TaskId { get; init; }
    public int TaskVersion { get; init; }
    public required string EdgeId { get; init; }
    public required string ConfigurationHash { get; init; }
    public required string ConfigurationSource { get; init; }
    public required string ConfigurationStatus { get; init; }
    public DateTimeOffset FirstObservedAt { get; init; }
    public DateTimeOffset LastObservedAt { get; init; }
    public DateTimeOffset RunOverlapStart { get; init; }
    public DateTimeOffset RunOverlapEnd { get; init; }
    public required string SampleEvidenceStatus { get; init; }
    public int SampleFrameCount { get; init; }
    public IReadOnlyList<string> RequiredDataItemCodes { get; init; } = [];
    public IReadOnlyList<string> ObservedDataItemCodes { get; init; } = [];
    public IReadOnlyList<string> NotObservedRequiredDataItemCodes { get; init; } = [];
}

public static class ExpectedConfigurationSampleEvidenceStatuses
{
    public const string Matched = "matched";
    public const string NoMatchingFrames = "no-matching-frames";
    public const string SummarizedUnverifiable = "summarized-unverifiable";
    public const string UnverifiableTaskVersion = "unverifiable-task-version";
    public const string ObservationHistoryTruncated = "observation-history-truncated";
    public const string CoverageResultTruncated = "coverage-result-truncated";
}

public sealed record DataReliabilityBaseline
{
    public DateTimeOffset GeneratedAt { get; init; }
    public string? SiteId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? EdgeId { get; init; }
    public string? EquipmentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public int MatchingCompletedRunCount { get; init; }
    public int MatchingObservedRunCount { get; init; }
    public int AnalyzedRunCount { get; init; }
    public int MissingExecutionRowCount { get; init; }
    public bool Truncated { get; init; }
    public bool TaskDeploymentObservationHistoryTruncated { get; init; }
    public bool ExpectedConfigurationCoverageTruncated { get; init; }
    public IReadOnlyList<ReliabilityRate> Rates { get; init; } = [];
    public IReadOnlyList<ContextFieldCoverage> ContextFields { get; init; } = [];
    public IReadOnlyList<ContextFactorSummary> ContextFactors { get; init; } = [];
    public IReadOnlyList<ContextFactorOverlap> ContextFactorOverlaps { get; init; } = [];
    public int UnidentifiableConfoundingCount { get; init; }
    public IReadOnlyList<ReliabilityExclusionCount> Exclusions { get; init; } = [];
    public IReadOnlyList<ReliabilityRunImpact> RunImpacts { get; init; } = [];
    public int DuplicateTimestampCount { get; init; }
    public int OutOfOrderCount { get; init; }
    public int SequenceGapCount { get; init; }
    public double? MaximumSampleGapMs { get; init; }
    public double? MaximumAbsoluteSourceClockOffsetMs { get; init; }

    public double? WorstRunP95PlatformIngestLatencyMs { get; init; }
    public double? MaximumPlatformIngestLatencyMs { get; init; }
    public int NegativePlatformIngestLatencyCount { get; init; }
}
