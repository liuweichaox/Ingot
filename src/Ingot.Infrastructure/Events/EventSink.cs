using System.Diagnostics;
using System.Text.Json;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Infrastructure.Events;

/// <summary>
///     事件写入口：先持久化事实，再执行可失败、可重建的 TSDB 投影。
/// </summary>
public sealed class EventSink : IEventSink
{
    private readonly IEventLog _eventLog;
    private readonly IQueueService _queue;
    private readonly IMetricsCollector? _metrics;
    private readonly ILogger<EventSink> _logger;
    private readonly EventOptions _options;
    private readonly IEventPersistenceHealth _health;

    public EventSink(
        IEventLog eventLog,
        IQueueService queue,
        IOptions<EventOptions> options,
        ILogger<EventSink> logger,
        IEventPersistenceHealth health,
        IMetricsCollector? metrics = null)
    {
        _eventLog = eventLog;
        _queue = queue;
        _logger = logger;
        _metrics = metrics;
        _options = options.Value;
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

        if (_options.EnableInfluxProjection)
            await ProjectToTelemetryAsync(persisted).ConfigureAwait(false);

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

    private async Task ProjectToTelemetryAsync(ProductionEvent evt)
    {
        try
        {
            var sourceCode = ExtractSourceCode(evt.Source);
            var message = DataMessage.CreateEventProjection(
                evt.EventId,
                sourceCode,
                evt.EventType,
                evt.Subject.Type,
                evt.Subject.Id,
                evt.CorrelationId,
                evt.OccurredAt);

            message.AddDataValue("event_id", evt.EventId);
            message.AddDataValue("seq", evt.Seq);
            message.AddDataValue("source", evt.Source);
            message.AddDataValue("type_version", evt.EventTypeVersion);
            message.AddDataValue("recorded_at", evt.RecordedAt);
            message.AddDataValue("context_json", JsonSerializer.Serialize(evt.Context));
            foreach (var pair in evt.Data)
                message.AddDataValue(pair.Key, pair.Value);

            await _queue.PublishAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "事件已落盘，但 TSDB 投影失败：EventId={EventId}, EventType={EventType}",
                evt.EventId,
                evt.EventType);
        }
    }

    private static string ExtractSourceCode(string source)
    {
        var segments = source.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 3 ? segments[2] : source;
    }
}
