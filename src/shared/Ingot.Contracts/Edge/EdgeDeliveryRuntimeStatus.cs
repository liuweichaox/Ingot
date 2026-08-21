namespace Ingot.Contracts.Edge;

public sealed record EdgeDeliveryRuntimeStatus
{
    public string State { get; init; } = "starting";
    public DateTimeOffset ObservedAt { get; init; }
    public long PendingEventCount { get; init; }
    public DateTimeOffset? OldestPendingEventAt { get; init; }
    public long? BacklogCapacityRows { get; init; }
    public double? BacklogCapacityUsedPercent { get; init; }
    public long? LocalStorageBytes { get; init; }
    public double? ShipmentRatePerSecond { get; init; }
    public double? EstimatedDrainSeconds { get; init; }
    public long? LastAcknowledgedSequence { get; init; }
    public long EventsShipped { get; init; }
    public DateTimeOffset? LastSuccessfulShipmentAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public int ConsecutiveFailures { get; init; }
    public long RecoveryCount { get; init; }
    public double? LastRecoveryDurationMs { get; init; }
    public string? LastError { get; init; }
}
