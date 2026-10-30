// 评估已上报采集任务的静默状态并维护七天的稀疏转移历史。
using System.Diagnostics;
using Ingot.Platform.Infrastructure.Services;

namespace Ingot.Platform.Worker;

public sealed class EdgeTaskFreshnessHistoryWorker(
    EdgeRegistry registry,
    EdgeTaskFreshnessWorkerMetrics metrics,
    ILogger<EdgeTaskFreshnessHistoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        var nextPruneAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await registry.EvaluateTaskFreshnessTransitionsAsync(now, stoppingToken)
                    .ConfigureAwait(false);
                stopwatch.Stop();
                metrics.RecordEvaluation(result, stopwatch.Elapsed, DateTimeOffset.UtcNow);
                if (result.TransitionCount > 0)
                {
                    logger.LogInformation(
                        "采集任务静默状态评估完成：节点 {EdgeCount} 个，任务 {TaskCount} 个，状态转移 {TransitionCount} 项，耗时 {DurationMs} ms。",
                        result.EdgeCount,
                        result.TaskCount,
                        result.TransitionCount,
                        stopwatch.Elapsed.TotalMilliseconds);
                }

                if (now >= nextPruneAt)
                {
                    await registry.PruneRuntimeStatusHistoryAsync(now, stoppingToken).ConfigureAwait(false);
                    nextPruneAt = now.AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                metrics.RecordFailure(DateTimeOffset.UtcNow);
                logger.LogError(exception, "采集任务静默状态评估或历史保留清理失败，Worker 将在下一周期重试。");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
