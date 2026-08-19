namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>
/// 生产运行的边界定义。系统根据运行事件确定每个运行的开始/结束/状态。
/// </summary>
public sealed record ExecutionBoundary
{
    /// <summary>生产运行的唯一标识（由平台生成）。</summary>
    public required string ExecutionId { get; init; }

    /// <summary>运行所属的生产单元。</summary>
    public required string SiteId { get; init; }

    /// <summary>运行所属的采集节点。</summary>
    public required string EdgeId { get; init; }

    /// <summary>运行的 ProductionEvent.ExecutionId，用于关联生产事件。</summary>
    public required string SourceExecutionId { get; init; }

    /// <summary>运行观察到的开始时刻（OccurredAt 最小值）。</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// 运行观察到的结束时刻。
    /// - 若运行未结束，为 null。
    /// - 结束时刻由 process.execution.completed 事件给出，或启发式推断（超时/无事件一段时间）。
    /// </summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>运行的生命周期状态。</summary>
    public ExecutionBoundaryStatus Status { get; init; } = ExecutionBoundaryStatus.InProgress;

    /// <summary>运行包含的事件数。</summary>
    public int EventCount { get; init; }

    /// <summary>运行包含的最小序号（Platform 的 IngestId）。</summary>
    public long MinIngestId { get; init; }

    /// <summary>运行包含的最大序号（Platform 的 IngestId）。</summary>
    public long MaxIngestId { get; init; }

    /// <summary>
    /// 运行边界的置信度。
    /// - Complete: 观察到明确的 process.execution.started 和 process.execution.completed。
    /// - InferredEnd: 运行有开始事件，但未见结束事件；用启发式（超时）推断结束。
    /// - Fragmented: 缺少开始或结束事件，从中间事件推断边界。
    /// </summary>
    public ExecutionBoundaryConfidence Confidence { get; init; } = ExecutionBoundaryConfidence.Complete;

    /// <summary>置信度的附加说明（例如为什么使用了启发式）。</summary>
    public string? ConfidenceReason { get; init; }

    /// <summary>所属 Edge 事件序列是否曾观察到缺口；一旦发现不得被后续批次清除。</summary>
    public bool GapDetected { get; init; }

    /// <summary>运行最后一次观察的时间（用于决定何时结束 InProgress 运行）。</summary>
    public DateTimeOffset LastObservedAt { get; init; }

    /// <summary>运行记录首次创建的时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>运行记录最后修改的时间（当边界被修正时）。</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>运行生命周期状态。</summary>
public enum ExecutionBoundaryStatus
{
    /// <summary>运行进行中，边界可能被修正。</summary>
    InProgress = 0,

    /// <summary>运行已完成。边界不再更新。</summary>
    Completed = 1,

    /// <summary>运行数据被标记为不可信（如严重的数据乱序或缺口），不应用于分析。</summary>
    Discarded = 2,
}

/// <summary>边界置信度等级。</summary>
public enum ExecutionBoundaryConfidence
{
    /// <summary>观察到明确的 process.execution.started 和 process.execution.completed 事件。</summary>
    Complete = 0,

    /// <summary>有开始事件和足够的运行事件，但未见结束事件；用启发式（如超时）推断结束时间。</summary>
    InferredEnd = 1,

    /// <summary>缺少开始或结束事件之一或两者；从中间事件和上下文推断。</summary>
    Fragmented = 2,
}
