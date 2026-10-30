// 定义绑定站点的边缘节点心跳和运行状态契约。
using Ingot.Contracts.Acquisition;

namespace Ingot.Contracts.Edge;

public sealed record EdgeHeartbeatRequest
{
    public required string SiteId { get; init; }

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
    public DateTimeOffset? AcquisitionReportedAt { get; init; }
    public string? AcquisitionState { get; init; }
    public DateTimeOffset? LastValidSnapshotAt { get; init; }
    public long ValidSnapshotCount { get; init; }
    public long EmittedEventCount { get; init; }
    public string? AcquisitionError { get; init; }
    public string? DeliveryState { get; init; }
    public long? PendingEventCount { get; init; }
    public DateTimeOffset? BacklogObservedAt { get; init; }
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
    public long? MaximumPendingEventCount { get; init; }
    public long BacklogObservationHeartbeatCount { get; init; }
    public long DistinctBacklogObservationCount { get; init; }
    public DateTimeOffset? LastBacklogObservedAt { get; init; }
}

public static class EdgeTaskFreshnessStates
{
    public const string Unknown = "unknown";
    public const string Unmonitored = "unmonitored";
    public const string WithinLimit = "within-limit";
    public const string Timeout = "timeout";
    public const string Removed = "removed";
}

public sealed record EdgeTaskFreshnessTransition
{
    public required string EdgeId { get; init; }
    public required string ConfigurationKey { get; init; }
    public string? PreviousState { get; init; }
    public required string State { get; init; }
    public int? PreviousThresholdMs { get; init; }
    public int? ThresholdMs { get; init; }
    public DateTimeOffset EffectiveAt { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public long? DurationMs { get; init; }
    public DateTimeOffset? LastValidSnapshotAt { get; init; }
}
