namespace Ingot.Contracts.Events;

/// <summary>面向生产与质量人员的周期级视图，不包含高频样本明细。</summary>
public sealed record CycleRecordSummary
{
    public required string CorrelationId { get; init; }
    public required string MachineId { get; init; }
    /// <summary>该运行实际收到事件的 Edge；正常运行通常只有一个，迁移或补传时可能有多个。</summary>
    public IReadOnlyList<string> EdgeIds { get; init; } = [];
    public required string Status { get; init; }
    public bool HasStarted { get; init; }
    public bool HasCompleted { get; init; }
    public bool LifecycleComplete { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public double? DurationMs { get; init; }
    public string? WorkpieceId { get; init; }
    public string? ProductSeries { get; init; }
    public string? ProductCode { get; init; }
    public string? RecipeId { get; init; }
    public string? RecipeVersion { get; init; }
    public string? ToolingInstallationId { get; init; }
    public string? ToolingId { get; init; }
    public string? MoldId { get; init; }
    public string? AssemblyRevisionId { get; init; }
    public string? AssemblyRevision { get; init; }
    public string? ExternalOrderRef { get; init; }
    public string? ExternalBatchRef { get; init; }
    public string? MaterialLotRef { get; init; }
    public int SampleCount { get; init; }
    public int ExpectedSampleCount { get; init; }
    /// <summary>旧接口兼容字段；新分析请使用 ProcessDataQuality。</summary>
    public double? SampleCompleteness { get; init; }
    public ProcessDataQualitySummary ProcessDataQuality { get; init; } = new();
    public int PhaseCount { get; init; }
    public string QualityStatus { get; init; } = "NOT_APPLICABLE";
    public string? InspectionPlanId { get; init; }
    public int? InspectionPlanVersion { get; init; }
    public string? InspectionPlanName { get; init; }
    public string? AnalysisPlanId { get; init; }
    public int? AnalysisPlanVersion { get; init; }
    public string? DataModelId { get; init; }
    public int? DataModelVersion { get; init; }
    public CycleAnalysisMaterialization AnalysisMaterialization { get; init; } = new();
    public int InspectionCount { get; init; }
    public int RequiredInspectionCount { get; init; }
    public int CompletedInspectionCount { get; init; }
    public int PendingReviewCount { get; init; }
    public IReadOnlyList<CyclePhaseSummary> Phases { get; init; } = [];
    public IReadOnlyList<CycleDataIssue> DataIssues { get; init; } = [];
}

public sealed record CyclePhaseSummary
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    /// <summary>同一阶段在一个周期内可重复出现，序号从 1 开始。</summary>
    public int Order { get; init; }
    /// <summary>stage_number 或 unknown。</summary>
    public string Source { get; init; } = "unknown";
    public int SampleCount { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
}

public sealed record CycleDataIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
}

public sealed record CycleRecordOverview
{
    public int CycleCount { get; init; }
    public int CompletedCount { get; init; }
    public int ActiveCount { get; init; }
    public int IncompleteCount { get; init; }
    public int SampleCompleteCount { get; init; }
    public int QualityCompleteCount { get; init; }
    public int IssueCycleCount { get; init; }
}

public sealed record CycleRecordQueryResult
{
    public IReadOnlyList<CycleRecordSummary> Data { get; init; } = [];
    public int Total { get; init; }
    public CycleRecordOverview Overview { get; init; } = new();
}
