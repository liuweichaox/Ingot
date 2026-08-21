using Ingot.Contracts.Acquisition;

namespace Ingot.Contracts.Edge;

public sealed record EdgeHeartbeatRequest
{
    public required string EdgeId { get; init; }

    public string? HostBaseUrl { get; init; }

    public string? LastError { get; init; }

    public EdgeAcquisitionRuntimeStatus? Acquisition { get; init; }

    public EdgeDeliveryRuntimeStatus? Delivery { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record EdgeRuntimeStatusHistoryItem
{
    public required string EdgeId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string? AcquisitionState { get; init; }
    public DateTimeOffset? LastValidSnapshotAt { get; init; }
    public long ValidSnapshotCount { get; init; }
    public long EmittedEventCount { get; init; }
    public string? AcquisitionError { get; init; }
    public string? DeliveryState { get; init; }
    public long PendingEventCount { get; init; }
    public DateTimeOffset? OldestPendingEventAt { get; init; }
    public double? BacklogCapacityUsedPercent { get; init; }
    public double? ShipmentRatePerSecond { get; init; }
    public string? DeliveryError { get; init; }
}

public sealed record EdgeRuntimeStatusInterval
{
    public required string EdgeId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public long SampleCount { get; init; }
    public string? AcquisitionState { get; init; }
    public string? AcquisitionError { get; init; }
    public string? DeliveryState { get; init; }
    public string? DeliveryError { get; init; }
    public long StartingValidSnapshotCount { get; init; }
    public long EndingValidSnapshotCount { get; init; }
    public long StartingEmittedEventCount { get; init; }
    public long EndingEmittedEventCount { get; init; }
    public long MaximumPendingEventCount { get; init; }
}
