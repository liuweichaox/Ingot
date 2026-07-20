using System.Diagnostics;
using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.Infrastructure.Events;

/// <summary>
///     协议无关的事件写入口：先持久化事实，再由 outbox 上报中心。
/// </summary>
public sealed class EventSink : IEventSink
{
    private readonly IEventLog _eventLog;
    private readonly IMetricsCollector? _metrics;
    private readonly ILogger<EventSink> _logger;
    private readonly IEventPersistenceHealth _health;

    public EventSink(
        IEventLog eventLog,
        ILogger<EventSink> logger,
        IEventPersistenceHealth health,
        IMetricsCollector? metrics = null)
    {
        _eventLog = eventLog;
        _logger = logger;
        _metrics = metrics;
        _health = health;
    }

    public async ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default)
    {
        if (!ProductionEventValidator.TryValidate(
                evt,
                requirePersistedSequence: false,
                out var validationError))
        {
            throw new ArgumentException(validationError, nameof(evt));
        }

        var stopwatch = Stopwatch.StartNew();
        var recorded = evt with { RecordedAt = DateTimeOffset.UtcNow };
        long seq;
        try
        {
            seq = await _eventLog.AppendAsync(recorded, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordPersistenceFailureMetric(evt.EventType);
            _health.ReportFailure(DateTimeOffset.UtcNow, ex);
            throw;
        }

        var persisted = recorded with { Seq = seq };
        _health.ReportSuccess(persisted.RecordedAt);
        stopwatch.Stop();
        RecordEmittedMetric(persisted.EventType, stopwatch.Elapsed.TotalMilliseconds);
        await RecordBacklogMetricAsync(ct).ConfigureAwait(false);

        return persisted;
    }

    private void RecordEmittedMetric(string eventType, double latencyMs)
    {
        try
        {
            _metrics?.RecordEventEmitted(eventType, latencyMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "事件已经落盘，但记录 event_emitted 指标失败；事实成立状态不受影响。");
        }
    }

    private void RecordPersistenceFailureMetric(string eventType)
    {
        try
        {
            _metrics?.RecordEventPersistenceFailure(eventType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "事件落盘失败，且记录持久化失败指标时发生异常。");
        }
    }

    private async Task RecordBacklogMetricAsync(CancellationToken ct)
    {
        if (_metrics is null)
            return;

        try
        {
            _metrics.RecordEventOutboxBacklog(
                await _eventLog.CountPendingAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "事件已经落盘，但读取 outbox backlog 指标失败；事实成立状态不受影响。");
        }
    }

}
