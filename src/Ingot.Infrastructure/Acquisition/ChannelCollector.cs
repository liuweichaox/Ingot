using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     通道采集器。根据配置从 Plc 读取数据，支持无条件和条件采集模式，将数据发布到队列。
/// </summary>
public class ChannelCollector : IChannelCollector
{
    private const string DiagnosticMeasurementSuffix = "_diagnostic";
    private readonly int _connectionCheckRetryDelayMs;
    private readonly IHeartbeatMonitor _heartbeatMonitor;
    private readonly IEventSink _events;
    private readonly string _edgeId;
    private readonly ILogger<ChannelCollector> _logger;
    private readonly IMetricsCollector? _metricsCollector;
    private readonly IQueueService _queue;
    private readonly IEdgeStateStore _stateManager;
    private readonly int _triggerWaitDelayMs;

    // 采集频率统计（per-channel，避免跨通道竞态）
    private readonly ConcurrentDictionary<string, (int Count, long LastTicks)> _rateStats = new();

    /// <summary>
    ///     初始化通道采集器。
    /// </summary>
    public ChannelCollector(
        IHeartbeatMonitor heartbeatMonitor,
        ILogger<ChannelCollector> logger,
        IQueueService queue,
        IEdgeStateStore stateManager,
        IEventSink events,
        IConfiguration configuration,
        IOptions<AcquisitionOptions> acquisitionOptions,
        IMetricsCollector? metricsCollector = null)
    {
        _heartbeatMonitor = heartbeatMonitor;
        _logger = logger;
        _queue = queue;
        _stateManager = stateManager;
        _events = events;
        _edgeId = configuration["Edge:EdgeId"]?.Trim() ?? Environment.MachineName;
        _metricsCollector = metricsCollector;

        var options = acquisitionOptions.Value.ChannelCollector;
        _connectionCheckRetryDelayMs = options.ConnectionCheckRetryDelayMs;
        _triggerWaitDelayMs = options.TriggerWaitDelayMs;
    }

    /// <summary>
    ///     按通道配置执行采集任务。支持 Always（持续）和 Conditional（边沿触发）两种模式。
    /// </summary>
    public async Task CollectAsync(DeviceConfig config, AcquisitionChannel dataAcquisitionChannel,
        IPlcDataAccessClient client, CancellationToken ct = default)
    {
        object? prevValue = null;
        while (!ct.IsCancellationRequested)
        {
            // 检查连接状态（快速检查，未连接时直接跳过，不延迟）
            if (!_heartbeatMonitor.TryGetConnectionHealth(config.SourceCode, out var isConnected) || !isConnected)
            {
                // 未连接时等待一小段时间再重试，避免CPU空转
                await Task.Delay(_connectionCheckRetryDelayMs, ct).ConfigureAwait(false);
                continue;
            }

            // 执行采集
            var timestamp = DateTimeOffset.UtcNow;
            if (dataAcquisitionChannel.AcquisitionMode == AcquisitionMode.Always)
                await HandleUnconditionalCollectionAsync(config, dataAcquisitionChannel, client, timestamp, ct)
                    .ConfigureAwait(false);
            else if (dataAcquisitionChannel.AcquisitionMode == AcquisitionMode.Conditional)
                prevValue = await HandleConditionalCollectionAsync(config, dataAcquisitionChannel, client,
                    timestamp, prevValue, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     处理无条件采集。读取数据并按配置频率延迟。
    /// </summary>
    private async Task HandleUnconditionalCollectionAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        await HandleUnconditionalEventAsync(config.SourceCode, channel, client, timestamp).ConfigureAwait(false);
        // AcquisitionInterval = 0 表示最高频率采集（无延迟），> 0 表示延迟指定毫秒数
        if (channel.AcquisitionInterval > 0) await Task.Delay(channel.AcquisitionInterval, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     处理条件采集。监控触发条件，触发后会调用 HandleStartEventAsync 和 HandleEndEventAsync。
    /// </summary>
    private async Task<object?> HandleConditionalCollectionAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp,
        object? prevValue,
        CancellationToken ct)
    {
        if (channel.ConditionalAcquisition == null) return prevValue;

        var conditionalAcq = channel.ConditionalAcquisition;
        if (string.IsNullOrWhiteSpace(conditionalAcq.Register) || string.IsNullOrWhiteSpace(conditionalAcq.DataType))
        {
            _logger.LogError("{SourceCode}-{ChannelCode}-{Measurement}:条件采集配置不完整，Register或DataType为空", config.SourceCode,
                channel.ChannelCode, channel.Measurement);
            await Task.Delay(_triggerWaitDelayMs, ct).ConfigureAwait(false);
            return prevValue;
        }

        // 读取触发寄存器的值
        var curr = await PlcValueAccessor.ReadAsync(client, conditionalAcq.Register, conditionalAcq.DataType)
            .ConfigureAwait(false);

        // 首次读取先做恢复判定，再建立基线，避免服务启动瞬间产生伪周期。
        if (prevValue == null)
        {
            await HandleRecoveryOnFirstSampleAsync(config, channel, client, timestamp, curr, ct)
                .ConfigureAwait(false);
            await Task.Delay(_triggerWaitDelayMs, ct).ConfigureAwait(false);
            return curr;
        }

        // v1 ConditionalAcquisition 映射为统一 EdgePair 规则，只保留旧通道的遥测发布职责。
        var compatibilityRule = CreateCompatibilityRule(conditionalAcq);
        var evaluation = EventRuleEvaluator.Evaluate(compatibilityRule, prevValue, curr);

        // 优先处理结束事件（如果同时触发，先结束当前周期，再开始新周期）
        if (evaluation.ShouldComplete)
            await HandleEndEventAsync(config, channel, timestamp, ct).ConfigureAwait(false);

        if (evaluation.ShouldStart)
            await HandleStartTriggerAsync(config, channel, client, timestamp, ct).ConfigureAwait(false);

        // 延迟并返回当前值用于下次比较
        await Task.Delay(_triggerWaitDelayMs, ct).ConfigureAwait(false);
        return curr;
    }

    private static EventRule CreateCompatibilityRule(ConditionalAcquisition conditional)
        => new()
        {
            Category = "cycle",
            Trigger = new EventRuleTrigger
            {
                Kind = EventTriggerKind.EdgePair,
                Tag = conditional.Register ?? string.Empty,
                DataType = conditional.DataType ?? string.Empty,
                StartTriggerMode = conditional.StartTriggerMode ?? AcquisitionTrigger.RisingEdge,
                EndTriggerMode = conditional.EndTriggerMode ?? AcquisitionTrigger.FallingEdge
            }
        };

    /// <summary>
    ///     处理开始触发：记录指标并执行开始事件。
    /// </summary>
    private async Task HandleStartTriggerAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await HandleStartEventAsync(config, channel, client, timestamp, ct).ConfigureAwait(false);
        sw.Stop();

        RecordCollectionMetrics(config.SourceCode, channel, sw.ElapsedMilliseconds);
    }

    private async Task HandleRecoveryOnFirstSampleAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp,
        object? currentValue,
        CancellationToken ct)
    {
        var sourceCode = config.SourceCode;
        var activeCycle = _stateManager.GetActiveCycle(sourceCode, channel.ChannelCode, channel.Measurement);
        var isActive = PlcValueAccessor.IsTriggerActive(currentValue);

        if (activeCycle != null && isActive)
        {
            await PublishRecoveryDiagnosticAsync(config, channel, client, timestamp, activeCycle, DiagnosticEventType.RecoveredStart, ct)
                .ConfigureAwait(false);
            return;
        }

        if (activeCycle != null && !isActive)
        {
            var interruptedCycle = _stateManager.EndCycle(sourceCode, channel.ChannelCode, channel.Measurement);
            if (interruptedCycle != null)
                await PublishRecoveryDiagnosticAsync(config, channel, client, timestamp, interruptedCycle, DiagnosticEventType.Interrupted, ct)
                    .ConfigureAwait(false);
            return;
        }

        if (activeCycle == null && isActive)
        {
            var recoveredCycle = _stateManager.StartCycle(sourceCode, channel.ChannelCode, channel.Measurement);
            await PublishRecoveryDiagnosticAsync(config, channel, client, timestamp, recoveredCycle, DiagnosticEventType.RecoveredStart, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     记录采集指标（延迟和频率）。
    /// </summary>
    private void RecordCollectionMetrics(string plcCode, AcquisitionChannel channel, long elapsedMilliseconds)
    {
        if (_metricsCollector == null) return;

        _metricsCollector.RecordCollectionLatency(plcCode, channel.ChannelCode, channel.Measurement,
            elapsedMilliseconds);

        var key = $"{plcCode}:{channel.ChannelCode}:{channel.Measurement}";
        var now = DateTimeOffset.UtcNow.Ticks;
        var updated = _rateStats.AddOrUpdate(key,
            _ => (1, now),
            (_, prev) =>
            {
                var elapsed = (now - prev.LastTicks) / (double)TimeSpan.TicksPerSecond;
                if (elapsed >= 1.0)
                {
                    var rate = (prev.Count + 1) / elapsed;
                    _metricsCollector.RecordCollectionRate(plcCode, channel.ChannelCode, channel.Measurement, rate);
                    return (0, now);
                }
                return (prev.Count + 1, prev.LastTicks);
            });
    }

    /// <summary>处理无条件采集事件：生成 CycleId → 读取数据 → 异步发布。</summary>
    private async Task HandleUnconditionalEventAsync(
        string plcCode,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp)
    {
        try
        {
            var cycleId = Guid.NewGuid().ToString();
            var dataMessage = DataMessage.Create(cycleId, channel.Measurement, plcCode, channel.ChannelCode,
                EventType.Data, timestamp);

            await PrepareMessageAsync(channel, client, dataMessage).ConfigureAwait(false);
            await PublishMessageAsync(plcCode, channel, dataMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metricsCollector?.RecordError(plcCode, channel.ChannelCode, channel.Measurement);
            _logger.LogError(ex, "{PlcCode}-{ChannelCode}-{Measurement}:采集异常: {Message}", plcCode, channel.ChannelCode,
                channel.Measurement, ex.Message);
        }
    }

    /// <summary>处理条件采集的开始事件：StartCycle → 读取数据 → 异步发布。</summary>
    private async Task HandleStartEventAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var sourceCode = config.SourceCode;
        try
        {
            var cycle = _stateManager.StartCycle(
                sourceCode,
                channel.ChannelCode,
                channel.Measurement);
            var dataMessage = DataMessage.Create(cycle.CycleId, channel.Measurement, sourceCode,
                channel.ChannelCode, EventType.Start, timestamp);
            await PrepareMessageAsync(channel, client, dataMessage).ConfigureAwait(false);
            await EmitCycleEventAsync(
                config,
                channel,
                "cycle.started",
                cycle,
                timestamp,
                dataMessage.DataValues,
                ct).ConfigureAwait(false);
            await PublishMessageAsync(sourceCode, channel, dataMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metricsCollector?.RecordError(sourceCode, channel.ChannelCode, channel.Measurement);
            _logger.LogError(ex, "{PlcCode}-{ChannelCode}-{Measurement}:采集异常: {Message}", sourceCode, channel.ChannelCode,
                channel.Measurement, ex.Message);
        }
    }

    /// <summary>
    ///     处理条件采集的结束事件：结束采集周期并发布 End 消息。
    /// </summary>
    private async Task HandleEndEventAsync(DeviceConfig config,
        AcquisitionChannel channel,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var sourceCode = config.SourceCode;
        try
        {
            // 结束采集周期，获取CycleId用于关联Start事件
            var cycle = _stateManager.EndCycle(sourceCode, channel.ChannelCode, channel.Measurement);
            if (cycle == null)
            {
                // 异常情况：找不到对应的cycle，记录警告并跳过
                _logger.LogError(
                    "{PlcCode}-{ChannelCode}-{Measurement} End事件触发但找不到对应的采集周期，可能Start事件未正确触发或系统重启导致状态丢失",
                    sourceCode, channel.ChannelCode, channel.Measurement);
                return;
            }

            await EmitCycleEventAsync(
                config,
                channel,
                "cycle.completed",
                cycle,
                timestamp,
                new Dictionary<string, object?>(),
                ct).ConfigureAwait(false);

            // 创建End事件数据点（时序数据库不支持Update，改为写入新数据点）
            var dataMessage = DataMessage.Create(cycle.CycleId, channel.Measurement, sourceCode,
                channel.ChannelCode, EventType.End, timestamp);
            await PublishMessageAsync(sourceCode, channel, dataMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{PlcCode}-{ChannelCode}-{Measurement}:采集异常: {Message}", sourceCode, channel.ChannelCode,
                channel.Measurement, ex.Message);
        }
    }

    private async Task PublishRecoveryDiagnosticAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DateTimeOffset timestamp,
        AcquisitionCycle cycle,
        DiagnosticEventType diagnosticType,
        CancellationToken ct)
    {
        var sourceCode = config.SourceCode;
        try
        {
            var dataMessage = DataMessage.CreateDiagnostic(
                cycle.CycleId,
                GetDiagnosticMeasurement(channel.Measurement),
                sourceCode,
                channel.ChannelCode,
                diagnosticType,
                timestamp);

            dataMessage.AddDataValue("source_measurement", channel.Measurement);
            await PrepareMessageAsync(channel, client, dataMessage).ConfigureAwait(false);
            await EmitCycleEventAsync(
                config,
                channel,
                diagnosticType == DiagnosticEventType.RecoveredStart
                    ? "diagnostic.cycle_recovered"
                    : "diagnostic.cycle_interrupted",
                cycle,
                timestamp,
                dataMessage.DataValues,
                ct).ConfigureAwait(false);
            await PublishMessageAsync(sourceCode, channel, dataMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metricsCollector?.RecordError(sourceCode, channel.ChannelCode, channel.Measurement);
            _logger.LogError(ex,
                "{PlcCode}-{ChannelCode}-{Measurement}:恢复诊断事件发布失败: {DiagnosticType}",
                sourceCode, channel.ChannelCode, channel.Measurement, diagnosticType);
        }
    }

    private async Task PrepareMessageAsync(
        AcquisitionChannel channel,
        IPlcDataAccessClient client,
        DataMessage dataMessage)
    {
        if (dataMessage.DiagnosticType.HasValue)
            dataMessage.AddDataValue("source_measurement", channel.Measurement);

        await ChannelMetricReader.ReadAsync(client, channel, dataMessage, _logger).ConfigureAwait(false);
        await MetricExpressionEvaluator.EvaluateAsync(dataMessage, channel.Metrics, _logger).ConfigureAwait(false);
    }

    /// <summary>
    ///     异步处理数据消息（表达式计算和发布），不阻塞采集循环。
    /// </summary>
    private async Task PublishMessageAsync(string plcCode, AcquisitionChannel channel,
        DataMessage dataMessage)
    {
        try
        {
            await _queue.PublishAsync(dataMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metricsCollector?.RecordError(plcCode, channel.ChannelCode, channel.Measurement);
            _logger.LogError(ex, "{PlcCode}-{ChannelCode}-{Measurement}:异步处理数据消息失败: {Message}", plcCode,
                channel.ChannelCode, channel.Measurement, ex.Message);
        }
    }

    private async Task EmitCycleEventAsync(
        DeviceConfig config,
        AcquisitionChannel channel,
        string eventType,
        AcquisitionCycle cycle,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken ct)
    {
        var subject = config.Asset ?? new ObjectRef("equipment", config.SourceCode);
        var data = values.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        data["measurement"] = channel.Measurement;
        data["channel_code"] = channel.ChannelCode;
        var evt = ProductionEvent.Create(
            eventType,
            occurredAt,
            $"edge/{_edgeId}/{config.SourceCode}/{channel.ChannelCode}",
            subject,
            cycle.CycleId,
            context: new Dictionary<string, string>(),
            data: data);

        await _events.EmitAsync(evt, ct).ConfigureAwait(false);
    }

    private static string GetDiagnosticMeasurement(string measurement) =>
        $"{measurement}{DiagnosticMeasurementSuffix}";

}
