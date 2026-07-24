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
    public IReadOnlyList<SignalDataCoverage> Signals { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed record SignalDataCoverage
{
    public required string Code { get; init; }
    public int ValidSampleCount { get; init; }
    public double Coverage { get; init; }
}

public sealed record CycleSignalFeature
{
    public required string Code { get; init; }
    /// <summary>特征定义版本；与定义哈希共同标识公式语义。</summary>
    public int DefinitionVersion { get; init; } = 1;
    /// <summary>规范化特征定义的 SHA-256。</summary>
    public string DefinitionHash { get; init; } = "";
    /// <summary>定义、输入点和计算窗口的 SHA-256，可用于复算核对。</summary>
    public string ComputationHash { get; init; } = "";
    public int InputPointCount { get; init; }
    /// <summary>空值表示整周期特征；非空值表示该工艺阶段内的特征。</summary>
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    /// <summary>同一阶段在一个周期内可重复出现，序号从 1 开始。</summary>
    public int? PhaseOrder { get; init; }
    /// <summary>cycle、event_tag、recipe_step 或 unknown。</summary>
    public string PhaseSource { get; init; } = "cycle";
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public double? Value { get; init; }
    public double ValidDurationMs { get; init; }
    public double Coverage { get; init; }
}

/// <summary>
///     周期分析结果的计算与持久化状态。源事件水位、配置版本和算法版本共同决定结果能否复用。
/// </summary>
public sealed record CycleAnalysisMaterialization
{
    /// <summary>query-time、materialized 或 cached。</summary>
    public string Status { get; init; } = "query-time";

    public string AlgorithmVersion { get; init; } = "stage-relative-v1";

    public DateTimeOffset? ComputedAt { get; init; }

    public long SourceMaxIngestId { get; init; }

    public int SourceEventCount { get; init; }
}

public sealed record CycleAnalysisBackfillRequest
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? ProductSeries { get; init; }
    public string? ProductCode { get; init; }
    public string? RecipeId { get; init; }
    public string? MachineId { get; init; }
    public int PageSize { get; init; } = 100;
}

public sealed record CycleAnalysisBackfillJob
{
    public Guid JobId { get; init; }
    public CycleAnalysisBackfillRequest Request { get; init; } = new();
    public string Status { get; init; } = "queued";
    public int TotalCycles { get; init; }
    public int ProcessedCycles { get; init; }
    public int MaterializedCycles { get; init; }
    public int FailedCycles { get; init; }
    public string? LastCorrelationId { get; init; }
    public string? Error { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record CycleFeatureAggregate
{
    public required string SignalCode { get; init; }
    public required string PhaseCode { get; init; }
    public required string FeatureCode { get; init; }
    public long CycleCount { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Average { get; init; }
    public double? StandardDeviation { get; init; }
    public double P10 { get; init; }
    public double Median { get; init; }
    public double P90 { get; init; }
}
