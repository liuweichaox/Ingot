
using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisRecomputeHostedService(
    IProcessExecutionAnalysisMaterializationStore materializations,
    IProcessExecutionService executions,
    Microsoft.Extensions.Options.IOptions<ProcessExecutionAnalysisRecomputeOptions> options,
    ILogger<ProcessExecutionAnalysisRecomputeHostedService> logger) : BackgroundService
{
    private readonly ProcessExecutionAnalysisRecomputeOptions _options = options.Value;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            var lease = await materializations.ClaimRecomputeAsync(TimeSpan.FromMinutes(5), stoppingToken)
                .ConfigureAwait(false);
            if (lease is null)
                continue;
            var error = await RecomputeAsync(lease.ExecutionId, stoppingToken).ConfigureAwait(false);
            if (error is null)
            {
                await materializations.CompleteRecomputeAsync(
                    lease.ExecutionId, lease.LeaseId, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, lease.AttemptCount - 1)));
                await materializations.RetryRecomputeAsync(
                    lease.ExecutionId, lease.LeaseId, delay, error, _options.MaxAttempts, stoppingToken).ConfigureAwait(false);
                if (lease.AttemptCount >= _options.MaxAttempts)
                    logger.LogError(
                        "过程执行分析重算达到最大尝试次数，转入失败终态：ExecutionId={ExecutionId}, Attempts={Attempts}",
                        lease.ExecutionId,
                        lease.AttemptCount);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task<string?> RecomputeAsync(string executionId, CancellationToken ct)
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
                    return null;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
            logger.LogWarning("过程执行 {ExecutionId} 的迟到事件重算暂未完成，将由持久化队列退避重试", executionId);
            return "分析物化在三次用例级尝试后仍未完成。";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "过程执行 {ExecutionId} 的迟到事件重算失败，将由持久化队列退避重试", executionId);
            return exception.Message;
        }
    }
}

public sealed class ProcessExecutionAnalysisRecomputeOptions
{
    public int MaxAttempts { get; init; } = 8;
}
