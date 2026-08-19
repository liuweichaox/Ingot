using Ingot.Contracts.Edge;

namespace Ingot.Edge.Application.Abstractions;

public sealed class EdgeDeliveryStatus
{
    private readonly object _gate = new();
    private long _pendingEventCount;
    private DateTimeOffset? _oldestPendingEventAt;
    private long? _backlogCapacityRows;
    private long? _localStorageBytes;
    private double? _shipmentRatePerSecond;
    private long? _lastAcknowledgedSequence;
    private long _eventsShipped;
    private DateTimeOffset? _lastSuccessfulShipmentAt;
    private DateTimeOffset? _lastFailureAt;
    private DateTimeOffset? _failureStartedAt;
    private int _consecutiveFailures;
    private long _recoveryCount;
    private double? _lastRecoveryDurationMs;
    private string? _lastError;
    private string? _blockedReason;

    public EdgeDeliveryRuntimeStatus Get()
    {
        lock (_gate)
        {
            var capacityUsed = _backlogCapacityRows > 0
                ? _pendingEventCount * 100d / _backlogCapacityRows.Value
                : 0;
            var state = _blockedReason is not null
                ? "blocked"
                : _consecutiveFailures > 0 || capacityUsed >= 80
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
                OldestPendingEventAt = _oldestPendingEventAt,
                BacklogCapacityRows = _backlogCapacityRows,
                BacklogCapacityUsedPercent = _backlogCapacityRows > 0
                    ? _pendingEventCount * 100d / _backlogCapacityRows.Value
                    : null,
                LocalStorageBytes = _localStorageBytes,
                ShipmentRatePerSecond = _shipmentRatePerSecond,
                EstimatedDrainSeconds = _shipmentRatePerSecond > 0
                    ? _pendingEventCount / _shipmentRatePerSecond.Value
                    : null,
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

    public void RecordBacklog(EventLogPendingStatistics statistics)
    {
        lock (_gate)
        {
            _pendingEventCount = Math.Max(0, statistics.Count);
            _oldestPendingEventAt = statistics.OldestRecordedAt;
            _backlogCapacityRows = statistics.CapacityRows;
            _localStorageBytes = statistics.StorageBytes;
        }
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

    public void RecordBlocked(string reason, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            _blockedReason = string.IsNullOrWhiteSpace(reason)
                ? "事件上送遇到不可恢复的 Edge 身份冲突。"
                : reason.Trim();
            _lastError = _blockedReason;
            _lastFailureAt = timestamp;
            _failureStartedAt ??= timestamp;
            _consecutiveFailures++;
        }
    }

    public void RecordSuccess(
        long acknowledgedSequence,
        int shipped,
        DateTimeOffset timestamp,
        double shipmentDurationMs = 0)
    {
        lock (_gate)
        {
            _lastAcknowledgedSequence = Math.Max(
                _lastAcknowledgedSequence ?? acknowledgedSequence,
                acknowledgedSequence);
            _eventsShipped += Math.Max(0, shipped);
            if (shipped > 0 && shipmentDurationMs > 0)
            {
                var observedRate = shipped / (shipmentDurationMs / 1000d);
                _shipmentRatePerSecond = _shipmentRatePerSecond.HasValue
                    ? _shipmentRatePerSecond.Value * 0.7 + observedRate * 0.3
                    : observedRate;
            }
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
            _blockedReason = null;
            _lastError = null;
        }
    }
}
