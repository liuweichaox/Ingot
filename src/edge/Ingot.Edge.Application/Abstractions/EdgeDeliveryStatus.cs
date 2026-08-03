using Ingot.Contracts.Edge;

namespace Ingot.Edge.Application.Abstractions;

public sealed class EdgeDeliveryStatus
{
    private readonly object _gate = new();
    private long _pendingEventCount;
    private long? _lastAcknowledgedSequence;
    private long _eventsShipped;
    private DateTimeOffset? _lastSuccessfulShipmentAt;
    private DateTimeOffset? _lastFailureAt;
    private DateTimeOffset? _failureStartedAt;
    private int _consecutiveFailures;
    private long _recoveryCount;
    private double? _lastRecoveryDurationMs;
    private string? _lastError;

    public EdgeDeliveryRuntimeStatus Get()
    {
        lock (_gate)
        {
            var state = _consecutiveFailures > 0
                ? "degraded"
                : _pendingEventCount > 0
                    ? "buffering"
                    : _lastSuccessfulShipmentAt.HasValue
                        ? "synchronized"
                        : "starting";
            return new EdgeDeliveryRuntimeStatus
            {
                State = state,
                ObservedAt = DateTimeOffset.UtcNow,
                PendingEventCount = _pendingEventCount,
                LastAcknowledgedSequence = _lastAcknowledgedSequence,
                EventsShipped = _eventsShipped,
                LastSuccessfulShipmentAt = _lastSuccessfulShipmentAt,
                LastFailureAt = _lastFailureAt,
                ConsecutiveFailures = _consecutiveFailures,
                RecoveryCount = _recoveryCount,
                LastRecoveryDurationMs = _lastRecoveryDurationMs,
                LastError = _lastError
            };
        }
    }

    public void RecordBacklog(long count)
    {
        lock (_gate)
            _pendingEventCount = Math.Max(0, count);
    }

    public void RecordFailure(string error, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            _failureStartedAt ??= timestamp;
            _lastFailureAt = timestamp;
            _consecutiveFailures++;
            _lastError = string.IsNullOrWhiteSpace(error) ? "事件上送失败。" : error.Trim();
        }
    }

    public void RecordSuccess(
        long acknowledgedSequence,
        int shipped,
        DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            _lastAcknowledgedSequence = Math.Max(
                _lastAcknowledgedSequence ?? acknowledgedSequence,
                acknowledgedSequence);
            _eventsShipped += Math.Max(0, shipped);
            _lastSuccessfulShipmentAt = timestamp;
            if (_failureStartedAt.HasValue)
            {
                _recoveryCount++;
                _lastRecoveryDurationMs = Math.Max(
                    0,
                    (timestamp - _failureStartedAt.Value).TotalMilliseconds);
            }
            _failureStartedAt = null;
            _consecutiveFailures = 0;
            _lastError = null;
        }
    }
}
