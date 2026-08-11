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
    /// <summary>设备源时间与 Edge 持久化时间之差的中位数；正值表示设备时间落后。</summary>
    public double? MedianSourceClockOffsetMs { get; init; }
    /// <summary>设备源时间与 Edge 持久化时间之间最大的绝对偏差。</summary>
    public double? MaximumAbsoluteSourceClockOffsetMs { get; init; }
    /// <summary>Edge 本地持久化到 Platform 摄入的中位延迟，包含断网上送积压时间。</summary>
    public double? MedianPlatformIngestLatencyMs { get; init; }
    public double? P95PlatformIngestLatencyMs { get; init; }
    public double? MaximumPlatformIngestLatencyMs { get; init; }
    /// <summary>Platform 摄入时间早于 Edge 持久化时间超过一秒的样本数，通常表示节点时钟异常。</summary>
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
    /// <summary>特征定义版本；与定义哈希共同标识公式语义。</summary>
    public int DefinitionVersion { get; init; } = 1;
    /// <summary>规范化特征定义的 SHA-256。</summary>
    public string DefinitionHash { get; init; } = "";
    /// <summary>定义、输入点和计算窗口的 SHA-256，可用于复算核对。</summary>
    public string ComputationHash { get; init; } = "";
    public int InputPointCount { get; init; }
    /// <summary>空值表示整次执行特征；非空值表示该工艺阶段内的特征。</summary>
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    /// <summary>同一阶段在一次过程执行内可重复出现，序号从 1 开始。</summary>
    public int? PhaseOrder { get; init; }
    /// <summary>execution、stage_number 或 unknown。</summary>
    public string PhaseSource { get; init; } = "execution";
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public double? Value { get; init; }
    public double ValidDurationMs { get; init; }
    public double Coverage { get; init; }
}

/// <summary>
///     过程执行分析结果的计算与持久化状态。源事件水位、配置版本和算法版本共同决定结果能否复用。
/// </summary>
public sealed record ProcessExecutionAnalysisMaterialization
{
    /// <summary>query-time、materialized 或 cached。</summary>
    public string Status { get; init; } = "query-time";

    /// <summary>生成该结果的算法版本；尚未计算时为 uncomputed。</summary>
    public string AlgorithmVersion { get; init; } = "uncomputed";

    public DateTimeOffset? ComputedAt { get; init; }

    public long SourceMinIngestId { get; init; }

    public long SourceMaxIngestId { get; init; }

    public int SourceEventCount { get; init; }

    /// <summary>参与本次计算的规范化原始事件集合 SHA-256，用于复算时核对精确输入。</summary>
    public string SourceContentHash { get; init; } = "";
}

public sealed record ProcessExecutionAnalysisBackfillRequest
{
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
