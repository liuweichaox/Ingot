namespace Ingot.Platform.Application.ProcessExecutions;

public sealed record ExecutionBoundary
{

    public required string ExecutionId { get; init; }

    public required string SiteId { get; init; }

    public required string EdgeId { get; init; }

    public required string SourceExecutionId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public ExecutionBoundaryStatus Status { get; init; } = ExecutionBoundaryStatus.InProgress;

    public int EventCount { get; init; }

    public long MinIngestId { get; init; }

    public long MaxIngestId { get; init; }

    public ExecutionBoundaryConfidence Confidence { get; init; } = ExecutionBoundaryConfidence.Complete;

    public string? ConfidenceReason { get; init; }

    public bool GapDetected { get; init; }

    public DateTimeOffset LastObservedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public enum ExecutionBoundaryStatus
{

    InProgress = 0,

    Completed = 1,

    Discarded = 2,
}

public enum ExecutionBoundaryConfidence
{

    Complete = 0,

    InferredEnd = 1,

    Fragmented = 2,
}
