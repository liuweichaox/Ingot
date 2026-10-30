// 汇总 Edge outbox 统计和事件上送结果，供心跳报告使用。
using System.Diagnostics;
using Ingot.Contracts.Edge;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>保留 outbox 最近一次成功统计和事件上送结果。</summary>
public sealed class EdgeDeliveryStatus
{
    private readonly object _gate = new();
    private long _pendingEventCount;
    private DateTimeOffset? _backlogObservedAt;
    private long? _backlogObservedTimestamp;
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
            var now = DateTimeOffset.UtcNow;
            var backlogObservationAge = _backlogObservedTimestamp.HasValue
                ? Stopwatch.GetElapsedTime(_backlogObservedTimestamp.Value)
                : (TimeSpan?)null;
            var backlogObserved = backlogObservationAge.HasValue && backlogObservationAge.Value >= TimeSpan.Zero
                && backlogObservationAge.Value <= TimeSpan.FromSeconds(30);
            var shipmentAge = _lastSuccessfulShipmentAt.HasValue ? now - _lastSuccessfulShipmentAt.Value : (TimeSpan?)null;
            var shipmentRateFresh = shipmentAge.HasValue && shipmentAge.Value >= TimeSpan.Zero
                && shipmentAge.Value <= TimeSpan.FromSeconds(30)
                && _consecutiveFailures == 0 && _blockedReason is null;
            var capacityUsed = backlogObserved && _backlogCapacityRows > 0
                ? _pendingEventCount * 100d / _backlogCapacityRows.Value
                : (double?)null;
            var state = _blockedReason is not null
                ? "blocked"
                : _consecutiveFailures > 0 || capacityUsed >= 80
                ? "degraded"
                : backlogObserved && _pendingEventCount > 0
                    ? "buffering"
                    : backlogObserved && _lastSuccessfulShipmentAt.HasValue
                        ? "synchronized"
                        : "starting";
            return new EdgeDeliveryRuntimeStatus
            {
                State = state,
                ObservedAt = now,
                BacklogObservedAt = _backlogObservedAt,
                BacklogObservationAgeMs = backlogObservationAge?.TotalMilliseconds,
                PendingEventCount = _pendingEventCount,
                OldestPendingEventAt = _oldestPendingEventAt,
                BacklogCapacityRows = _backlogCapacityRows,
                BacklogCapacityUsedPercent = backlogObserved && _backlogCapacityRows > 0
                    ? _pendingEventCount * 100d / _backlogCapacityRows.Value
                    : null,
                LocalStorageBytes = _localStorageBytes,
                ShipmentRatePerSecond = _shipmentRatePerSecond,
                EstimatedDrainSeconds = backlogObserved && _pendingEventCount > 0 && shipmentRateFresh && _shipmentRatePerSecond > 0
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
        {
            _pendingEventCount = Math.Max(0, count);
            _backlogObservedAt = DateTimeOffset.UtcNow;
            _backlogObservedTimestamp = Stopwatch.GetTimestamp();
            _oldestPendingEventAt = null;
            _backlogCapacityRows = null;
            _localStorageBytes = null;
        }
    }

    public void RecordBacklog(EventLogPendingStatistics statistics)
    {
        lock (_gate)
        {
            _pendingEventCount = Math.Max(0, statistics.Count);
            _backlogObservedAt = DateTimeOffset.UtcNow;
            _backlogObservedTimestamp = Stopwatch.GetTimestamp();
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
