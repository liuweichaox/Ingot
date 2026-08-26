// 定义过程数据质量、站点回填任务和特征聚合的传输契约。
namespace Ingot.Contracts.Events;

public static class ProcessDataStatuses
{
    public const string Available = "available";
    public const string Degraded = "degraded";
    public const string Unavailable = "unavailable";
}

public sealed record ProcessDataQualitySummary
{
    public string Status { get; init; } = ProcessDataStatuses.Unavailable;
    public int SampleCount { get; init; }
    public double? MedianIntervalMs { get; init; }
    public double? P95IntervalMs { get; init; }
    public double? MaximumGapMs { get; init; }
    public int DuplicateTimestampCount { get; init; }
    public int OutOfOrderCount { get; init; }
    public int SequenceGapCount { get; init; }

    public double? MedianSourceClockOffsetMs { get; init; }

    public double? MaximumAbsoluteSourceClockOffsetMs { get; init; }

    public double? MedianPlatformIngestLatencyMs { get; init; }
    public double? P95PlatformIngestLatencyMs { get; init; }
    public double? MaximumPlatformIngestLatencyMs { get; init; }

    public int NegativePlatformIngestLatencyCount { get; init; }
    public IReadOnlyList<SignalDataCoverage> Signals { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed record SignalDataCoverage
{
    public required string Code { get; init; }
    public int ValidSampleCount { get; init; }
    public double Coverage { get; init; }
}

public sealed record ProcessSignalFeature
{
    public required string Code { get; init; }

    public int DefinitionVersion { get; init; } = 1;

    public string DefinitionHash { get; init; } = "";

    public string ComputationHash { get; init; } = "";
    public int InputPointCount { get; init; }

    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }

    public int? PhaseOrder { get; init; }

    public string PhaseSource { get; init; } = "execution";
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public double? Value { get; init; }
    public double ValidDurationMs { get; init; }
    public double Coverage { get; init; }
}

public sealed record ProcessExecutionAnalysisMaterialization
{

    public string Status { get; init; } = "query-time";

    public string AlgorithmVersion { get; init; } = "uncomputed";

    public DateTimeOffset? ComputedAt { get; init; }

    public long SourceMinIngestId { get; init; }

    public long SourceMaxIngestId { get; init; }

    public int SourceEventCount { get; init; }

    public string SourceContentHash { get; init; } = "";
}

public sealed record ProcessExecutionAnalysisBackfillRequest
{
    public string SiteId { get; init; } = string.Empty;
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? ProductFamilyCode { get; init; }
    public string? ProductCode { get; init; }
    public string? ProcessSpecificationId { get; init; }
    public string? EquipmentId { get; init; }
    public int PageSize { get; init; } = 100;
}

public sealed record ProcessExecutionAnalysisBackfillJob
{
    public Guid JobId { get; init; }
    public ProcessExecutionAnalysisBackfillRequest Request { get; init; } = new();
    public string Status { get; init; } = "queued";
    public int TotalProcessExecutions { get; init; }
    public int ProcessedProcessExecutions { get; init; }
    public int MaterializedProcessExecutions { get; init; }
    public int FailedProcessExecutions { get; init; }
    public string? LastExecutionId { get; init; }
    public string? Error { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record ProcessExecutionFeatureAggregate
{
    public required string SignalCode { get; init; }
    public required string PhaseCode { get; init; }
    public required string FeatureCode { get; init; }
    public long ProcessExecutionCount { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Average { get; init; }
    public double? StandardDeviation { get; init; }
    public double P10 { get; init; }
    public double Median { get; init; }
    public double P90 { get; init; }
}
