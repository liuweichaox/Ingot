namespace Ingot.Contracts.Edge;

/// <summary>Edge 生产事件 outbox 向 Platform 可靠上送的主动心跳状态。</summary>
public sealed record EdgeDeliveryRuntimeStatus
{
    public string State { get; init; } = "starting";
    public DateTimeOffset ObservedAt { get; init; }
    public long PendingEventCount { get; init; }
    public long? LastAcknowledgedSequence { get; init; }
    public long EventsShipped { get; init; }
    public DateTimeOffset? LastSuccessfulShipmentAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public int ConsecutiveFailures { get; init; }
    public long RecoveryCount { get; init; }
    public double? LastRecoveryDurationMs { get; init; }
    public string? LastError { get; init; }
}
