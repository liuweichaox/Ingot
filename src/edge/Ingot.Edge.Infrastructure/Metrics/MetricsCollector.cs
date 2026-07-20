using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.Infrastructure.Metrics;

/// <summary>
///     基于 System.Diagnostics.Metrics 的指标收集器实现
/// </summary>
public class MetricsCollector : IMetricsCollector
{
    private readonly Histogram<double> _batchWriteEfficiencyHistogram;
    private readonly Histogram<double> _collectionLatencyHistogram;
    private readonly Histogram<double> _collectionRateHistogram;
    private readonly Histogram<double> _connectionDurationHistogram;
    private readonly Counter<long> _connectionStatusCounter;
    private readonly UpDownCounter<long> _contextStateEntriesGauge;
    private readonly Counter<long> _errorCounter;
    private readonly Counter<long> _eventEmittedCounter;
    private readonly Counter<long> _eventBacklogDroppedCounter;
    private readonly Counter<long> _eventPersistenceFailureCounter;
    private readonly Counter<long> _eventShipFailureCounter;
    private readonly Counter<long> _eventsShippedCounter;
    private readonly Histogram<double> _eventEmitLatencyHistogram;
    private readonly Histogram<double> _eventShipLatencyHistogram;
    private readonly UpDownCounter<long> _eventOutboxBacklogGauge;
    private readonly Meter _meter;
    private readonly Histogram<double> _processingLatencyHistogram;
    private readonly Histogram<int> _queueDepthHistogram;
    private readonly Histogram<double> _writeLatencyHistogram;
    private long _contextStateEntries;
    private long _eventOutboxBacklog;

    public MetricsCollector()
    {
        _meter = new Meter("Ingot", "1.0.0");

        // 采集延迟指标
        _collectionLatencyHistogram = _meter.CreateHistogram<double>(
            "ingot.telemetry.collection_latency_ms",
            "ms",
            "采集延迟（从数据源读取到写入数据库的时间，毫秒）");

        // 采集频率指标
        _collectionRateHistogram = _meter.CreateHistogram<double>(
            "ingot.telemetry.collection_rate",
            "points/s",
            "采集频率（每秒采集的数据点数）");

        // 队列深度指标（包括 Channel 待读取 + 批量积累）
        _queueDepthHistogram = _meter.CreateHistogram<int>(
            "ingot.telemetry.queue_depth",
            "messages",
            "队列深度（Channel待读取 + 批量积累的待处理消息总数）");

        // 处理延迟指标
        _processingLatencyHistogram = _meter.CreateHistogram<double>(
            "ingot.telemetry.processing_latency_ms",
            "ms",
            "处理延迟（队列处理延迟，毫秒）");

        // 写入延迟指标
        _writeLatencyHistogram = _meter.CreateHistogram<double>(
            "ingot.telemetry.write_latency_ms",
            "ms",
            "写入延迟（数据库写入延迟，毫秒）");

        // 批量写入效率指标
        _batchWriteEfficiencyHistogram = _meter.CreateHistogram<double>(
            "ingot.telemetry.batch_write_efficiency",
            "points/ms",
            "批量写入效率（批量大小/写入耗时）");

        // 错误计数
        _errorCounter = _meter.CreateCounter<long>(
            "ingot.telemetry.errors_total",
            "errors",
            "错误总数（按设备/通道统计）");

        // 连接状态计数
        _connectionStatusCounter = _meter.CreateCounter<long>(
            "ingot.source.connection_status_changes_total",
            "changes",
            "连接状态变化总数");

        // 连接持续时间
        _connectionDurationHistogram = _meter.CreateHistogram<double>(
            "ingot.source.connection_duration_seconds",
            "seconds",
            "连接持续时间（秒）");

        _eventEmittedCounter = _meter.CreateCounter<long>(
            "event_emitted_total",
            "events",
            "已经写入边缘不可变日志的生产事件总数");

        _eventEmitLatencyHistogram = _meter.CreateHistogram<double>(
            "event_emit_latency_ms",
            "ms",
            "生产事件从发射到本地持久化完成的延迟");

        _eventOutboxBacklogGauge = _meter.CreateUpDownCounter<long>(
            "event_outbox_backlog",
            "events",
            "事件 outbox 当前待上行数量");

        _eventBacklogDroppedCounter = _meter.CreateCounter<long>(
            "event_backlog_dropped_total",
            "events",
            "outbox 达到硬上限后显式丢弃的生产事件总数");

        _contextStateEntriesGauge = _meter.CreateUpDownCounter<long>(
            "context_state_entries",
            "entries",
            "当前持久化的资产业务上下文项数量");

        _eventPersistenceFailureCounter = _meter.CreateCounter<long>(
            "event_persistence_failures_total",
            "failures",
            "生产事件本地持久化失败总数");

        _eventShipFailureCounter = _meter.CreateCounter<long>(
            "event_ship_failures_total",
            "failures",
            "生产事件上行失败总数");

        _eventsShippedCounter = _meter.CreateCounter<long>(
            "event_shipped_total",
            "events",
            "中心已确认的生产事件总数");

        _eventShipLatencyHistogram = _meter.CreateHistogram<double>(
            "event_ship_latency_ms",
            "ms",
            "生产事件批次上行并取得确认的延迟");
    }

    public void RecordCollectionLatency(string sourceCode, string? channelCode, string measurement, double latencyMs)
    {
        var tags = new TagList
        {
            { "source_code", sourceCode },
            { "measurement", measurement }
        };
        if (!string.IsNullOrEmpty(channelCode))
            tags.Add("channel_code", channelCode);
        _collectionLatencyHistogram.Record(latencyMs, tags);
    }

    public void RecordCollectionRate(string sourceCode, string? channelCode, string measurement, double pointsPerSecond)
    {
        var tags = new TagList
        {
            { "source_code", sourceCode },
            { "measurement", measurement }
        };
        if (!string.IsNullOrEmpty(channelCode))
            tags.Add("channel_code", channelCode);
        _collectionRateHistogram.Record(pointsPerSecond, tags);
    }

    public void RecordQueueDepth(int depth)
    {
        _queueDepthHistogram.Record(depth);
    }

    public void RecordProcessingLatency(double latencyMs)
    {
        _processingLatencyHistogram.Record(latencyMs);
    }

    public void RecordWriteLatency(string measurement, double latencyMs)
    {
        _writeLatencyHistogram.Record(latencyMs, new TagList { { "measurement", measurement } });
    }

    public void RecordBatchWriteEfficiency(int batchSize, double latencyMs)
    {
        if (latencyMs > 0)
        {
            var efficiency = batchSize / latencyMs; // points per millisecond
            _batchWriteEfficiencyHistogram.Record(efficiency);
        }
    }

    public void RecordError(string sourceCode, string? channelCode = null, string? measurement = null)
    {
        var tags = new TagList { { "source_code", sourceCode } };
        if (!string.IsNullOrEmpty(channelCode))
            tags.Add("channel_code", channelCode);
        if (!string.IsNullOrEmpty(measurement))
            tags.Add("measurement", measurement);
        _errorCounter.Add(1, tags);
    }

    public void RecordConnectionStatus(string sourceCode, bool isConnected)
    {
        _connectionStatusCounter.Add(1, new TagList
        {
            { "source_code", sourceCode },
            { "status", isConnected ? "connected" : "disconnected" }
        });
    }

    public void RecordConnectionDuration(string sourceCode, double durationSeconds)
    {
        _connectionDurationHistogram.Record(durationSeconds, new TagList { { "source_code", sourceCode } });
    }

    public void RecordEventEmitted(string eventType, double latencyMs)
    {
        var tags = new TagList { { "event_type", eventType } };
        _eventEmittedCounter.Add(1, tags);
        _eventEmitLatencyHistogram.Record(latencyMs, tags);
    }

    public void RecordEventOutboxBacklog(long count)
    {
        var normalized = Math.Max(0, count);
        var previous = Interlocked.Exchange(ref _eventOutboxBacklog, normalized);
        var delta = normalized - previous;
        if (delta != 0)
            _eventOutboxBacklogGauge.Add(delta);
    }

    public void RecordEventBacklogDropped(long count)
    {
        if (count > 0)
            _eventBacklogDroppedCounter.Add(count);
    }

    public void RecordContextStateEntries(long count)
    {
        var normalized = Math.Max(0, count);
        var previous = Interlocked.Exchange(ref _contextStateEntries, normalized);
        var delta = normalized - previous;
        if (delta != 0)
            _contextStateEntriesGauge.Add(delta);
    }

    public void RecordEventPersistenceFailure(string eventType)
    {
        _eventPersistenceFailureCounter.Add(1, new TagList { { "event_type", eventType } });
    }

    public void RecordEventShipFailure(string edgeId)
    {
        _eventShipFailureCounter.Add(1, new TagList { { "edge_id", edgeId } });
    }

    public void RecordEventsShipped(string edgeId, int count, double latencyMs)
    {
        var tags = new TagList { { "edge_id", edgeId } };
        _eventsShippedCounter.Add(count, tags);
        _eventShipLatencyHistogram.Record(latencyMs, tags);
    }
}
