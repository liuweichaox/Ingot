using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Infrastructure.Queues;

/// <summary>
///     消息队列实现：批量聚合后直写存储。存储写入失败时记录错误并丢弃当前批次。
/// </summary>
public class QueueService : IQueueService
{
    private readonly object _batchLock = new();
    private readonly Dictionary<string, List<DataMessage>> _dataBatchMap = new();
    private readonly BatchSizeResolver _batchSizeResolver;
    private readonly IDataStorageService _storage;
    private readonly Timer? _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private readonly ILogger<QueueService> _logger;
    private readonly IMetricsCollector? _metricsCollector;
    private int _disposeStarted;

    public QueueService(
        IDataStorageService storage,
        ILogger<QueueService> logger,
        IOptions<AcquisitionOptions> acquisitionOptions,
        IDeviceConfigService deviceConfigService,
        IMetricsCollector? metricsCollector = null)
    {
        _storage = storage;
        _logger = logger;
        _metricsCollector = metricsCollector;
        _batchSizeResolver = new BatchSizeResolver(deviceConfigService, logger);

        var flushInterval = TimeSpan.FromSeconds(acquisitionOptions.Value.QueueService.FlushIntervalSeconds);
        _flushTimer = new Timer(FlushBatches, null, flushInterval, flushInterval);
    }

    public async Task PublishAsync(DataMessage dataMessage)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await StoreDataPointAsync(dataMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _metricsCollector?.RecordError(dataMessage.SourceCode, dataMessage.ChannelCode, dataMessage.Measurement);
            _logger.LogError(ex, "处理消息失败: {Message}", ex.Message);
        }
        finally
        {
            sw.Stop();
            _metricsCollector?.RecordQueueDepth(GetTotalQueueDepth());
            _metricsCollector?.RecordProcessingLatency(sw.ElapsedMilliseconds);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _flushTimer?.Dispose();

        await _flushSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var remaining = TakePendingBatches(clearAll: true);
            foreach (var batch in remaining)
                await PersistBatchAsync(batch.Measurement, batch.Messages).ConfigureAwait(false);
        }
        finally
        {
            _flushSemaphore.Release();
            _flushSemaphore.Dispose();
        }
    }

    /// <summary>定时刷新未达 BatchSize 的积压批次。</summary>
    private void FlushBatches(object? state)
    {
        _ = FlushPendingBatchesAsync();
    }

    private int GetTotalQueueDepth()
    {
        int batchDepth;
        lock (_batchLock) { batchDepth = _dataBatchMap.Values.Sum(list => list.Count); }
        return batchDepth;
    }

    private int GetBatchSize(string? sourceCode, string? channelCode, string measurement)
        => _batchSizeResolver.GetBatchSize(sourceCode, channelCode, measurement);

    /// <summary>
    ///     按 sourceCode:channelCode:measurement 批量聚合，达到 BatchSize 后写入存储。
    ///     BatchSize ≤ 1 时立即写入。
    /// </summary>
    private async Task StoreDataPointAsync(DataMessage msg)
    {
        var batchKey = $"{msg.SourceCode}:{msg.ChannelCode ?? "unknown"}:{msg.Measurement}";
        var batchSize = GetBatchSize(msg.SourceCode, msg.ChannelCode, msg.Measurement);

        PendingBatch? batchToSave = null;
        lock (_batchLock)
        {
            if (!_dataBatchMap.TryGetValue(batchKey, out var batch))
            {
                batch = new List<DataMessage>();
                _dataBatchMap[batchKey] = batch;
            }

            batch.Add(msg);
            if (batchSize <= 1 || batch.Count >= batchSize)
            {
                batchToSave = new PendingBatch(batchKey, msg.Measurement, [.. batch]);
                _dataBatchMap.Remove(batchKey);
            }
        }

        if (batchToSave != null)
            await PersistBatchAsync(batchToSave.Measurement, batchToSave.Messages).ConfigureAwait(false);
    }

    private List<PendingBatch> TakePendingBatches(bool clearAll)
    {
        lock (_batchLock)
        {
            var batches = _dataBatchMap
                .Where(kvp => kvp.Value.Count > 0)
                .Select(kvp => new PendingBatch(kvp.Key, kvp.Value[0].Measurement, [.. kvp.Value]))
                .ToList();

            if (clearAll)
            {
                foreach (var batch in batches)
                    _dataBatchMap.Remove(batch.BatchKey);
            }

            return batches;
        }
    }

    private async Task FlushPendingBatchesAsync()
    {
        if (!await _flushSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            _logger.LogDebug("上一轮定时刷新仍在执行，跳过本轮 Flush。");
            return;
        }

        try
        {
            var batchesToFlush = TakePendingBatches(clearAll: true);
            foreach (var batch in batchesToFlush)
                await PersistBatchAsync(batch.Measurement, batch.Messages).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定时刷新批次失败: {Message}", ex.Message);
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    private async Task PersistBatchAsync(string measurement, List<DataMessage> messages)
    {
        if (messages.Count == 0)
            return;

        try
        {
            var success = await _storage.SaveBatchAsync(messages).ConfigureAwait(false);
            if (success)
                return;

            LogDroppedBatch(measurement, messages, null);
        }
        catch (Exception ex)
        {
            LogDroppedBatch(measurement, messages, ex);
        }
    }

    private void LogDroppedBatch(string measurement, List<DataMessage> messages, Exception? ex)
    {
        var first = messages[0];
        _metricsCollector?.RecordError(first.SourceCode, first.ChannelCode, measurement);

        if (ex == null)
        {
            _logger.LogWarning(
                "存储写入失败，批次已丢弃: {Measurement}, Count={Count}, SourceCode={SourceCode}, ChannelCode={ChannelCode}",
                measurement,
                messages.Count,
                first.SourceCode,
                first.ChannelCode);
            return;
        }

        _logger.LogError(
            ex,
            "存储写入异常，批次已丢弃: {Measurement}, Count={Count}, SourceCode={SourceCode}, ChannelCode={ChannelCode}",
            measurement,
            messages.Count,
            first.SourceCode,
            first.ChannelCode);
    }
}
