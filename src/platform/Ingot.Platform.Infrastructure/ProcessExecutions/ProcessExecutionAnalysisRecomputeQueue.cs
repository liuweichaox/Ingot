using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisRecomputeQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    public void Enqueue(IEnumerable<string> executionIds)
    {
        foreach (var id in executionIds.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = id.Trim();
            if (_pending.TryAdd(normalized, 0))
                _channel.Writer.TryWrite(normalized);
        }
    }

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    public void Complete(string executionId)
        => _pending.TryRemove(executionId, out _);
}

public sealed class ProcessExecutionAnalysisRecomputeHostedService(
    ProcessExecutionAnalysisRecomputeQueue queue,
    IProcessExecutionAnalysisMaterializationStore materializations,
    IProcessExecutionService executions,
    ILogger<ProcessExecutionAnalysisRecomputeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueueDirtyAsync(stoppingToken).ConfigureAwait(false);
        var rescan = RescanAsync(stoppingToken);
        try
        {
            await foreach (var executionId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                await RecomputeAsync(executionId, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            await rescan.ConfigureAwait(false);
        }
    }

    private async Task RescanAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            await EnqueueDirtyAsync(ct).ConfigureAwait(false);
    }

    private async Task EnqueueDirtyAsync(CancellationToken ct)
    {
        var dirty = await materializations.ListDirtyExecutionIdsAsync(500, ct).ConfigureAwait(false);
        queue.Enqueue(dirty);
    }

    private async Task RecomputeAsync(string executionId, CancellationToken ct)
    {
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var result = await executions.QueryAsync(
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    executionId,
                    "completed",
                    1,
                    0,
                    null,
                    ct).ConfigureAwait(false);
                if (result.Data.Count == 0 ||
                    result.Data[0].AnalysisMaterialization.Status is "materialized" or "cached")
                    return;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
            logger.LogWarning("过程执行 {ExecutionId} 的迟到事件重算暂未完成，将由脏数据扫描再次尝试", executionId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "过程执行 {ExecutionId} 的迟到事件重算失败，将由脏数据扫描再次尝试", executionId);
        }
        finally
        {
            queue.Complete(executionId);
        }
    }
}
