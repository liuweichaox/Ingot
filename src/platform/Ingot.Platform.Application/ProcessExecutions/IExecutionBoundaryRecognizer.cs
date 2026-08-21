using Ingot.Domain.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

public interface IExecutionBoundaryRecognizer
{

    Task<IReadOnlyList<ExecutionBoundary>> RecognizeBoundariesAsync(
        string siteId,
        string edgeId,
        IReadOnlyList<ProductionEvent> events,
        ExecutionBoundaryRecognitionOptions options,
        CancellationToken ct);

    ExecutionBoundaryAdjustment AdjustForLateArrival(
        ExecutionBoundary existingBoundary,
        ProductionEvent lateArrivalEvent,
        ExecutionBoundaryRecognitionOptions options);

    ExecutionBoundary MarkGapDetected(ExecutionBoundary boundary, string gapDescription);
}

public sealed class ExecutionBoundaryRecognitionOptions
{

    public TimeSpan ExecutionTimeoutWithoutEndEvent { get; set; } = TimeSpan.FromHours(10);

    public TimeSpan LateArrivalThreshold { get; set; } = TimeSpan.FromHours(1);

    public bool RequireExplicitStartEnd { get; set; } = false;

    public long MaxSeqDisorderTolerance { get; set; } = 500;
}

public sealed record ExecutionBoundaryAdjustment
{

    public required ExecutionBoundary AdjustedExisting { get; init; }

    public ExecutionBoundary? NewBoundary { get; init; }
}
