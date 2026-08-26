// 消费迟到数据重算租约，并在站点归属不唯一时进入失败终态。
using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisRecomputeHostedService(
    IProcessExecutionAnalysisMaterializationStore materializations,
    IProcessExecutionAnalysisRecomputeExecutor executor,
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
            var attempt = await RecomputeAsync(lease.ExecutionId, stoppingToken).ConfigureAwait(false);
            if (attempt.Succeeded)
            {
                await materializations.CompleteRecomputeAsync(
                    lease.ExecutionId, lease.LeaseId, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                var delay = attempt.Terminal
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, lease.AttemptCount - 1)));
                await materializations.RetryRecomputeAsync(
                    lease.ExecutionId,
                    lease.LeaseId,
                    delay,
                    attempt.Error ?? "过程执行分析重算失败。",
                    attempt.Terminal ? lease.AttemptCount : _options.MaxAttempts,
                    stoppingToken).ConfigureAwait(false);
                if (attempt.Terminal)
                {
                    logger.LogError(
                        "过程执行分析重算因站点边界不安全进入失败终态：ExecutionId={ExecutionId}, Error={Error}",
                        lease.ExecutionId,
                        attempt.Error);
                }
                else if (lease.AttemptCount >= _options.MaxAttempts)
                {
                    logger.LogError(
                        "过程执行分析重算达到最大尝试次数，转入失败终态：ExecutionId={ExecutionId}, Attempts={Attempts}",
                        lease.ExecutionId,
                        lease.AttemptCount);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task<ProcessExecutionAnalysisRecomputeAttempt> RecomputeAsync(
        string executionId,
        CancellationToken ct)
    {
        try
        {
            var sites = await materializations.ResolveExecutionSitesAsync(executionId, ct).ConfigureAwait(false);
            if (sites.Count == 0)
            {
                return ProcessExecutionAnalysisRecomputeAttempt.TerminalFailure(
                    "未找到过程执行的站点归属；拒绝无站点物化。");
            }
            if (sites.Count != 1)
            {
                return ProcessExecutionAnalysisRecomputeAttempt.TerminalFailure(
                    "同一过程执行标识出现在多个站点；拒绝写入未包含站点的物化键。");
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var outcome = await executor.RecomputeAnalysisAsync(
                    executionId, sites[0], ct).ConfigureAwait(false);
                if (outcome == ProcessExecutionAnalysisRecomputeOutcome.Completed)
                    return ProcessExecutionAnalysisRecomputeAttempt.Success;
                if (outcome == ProcessExecutionAnalysisRecomputeOutcome.Unsafe)
                {
                    return ProcessExecutionAnalysisRecomputeAttempt.TerminalFailure(
                        "保存前站点唯一性复核失败；拒绝物化。 ");
                }
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
            logger.LogWarning("过程执行 {ExecutionId} 的迟到事件重算暂未完成，将由持久化队列退避重试", executionId);
            return ProcessExecutionAnalysisRecomputeAttempt.RetryableFailure(
                "分析物化在三次用例级尝试后仍未完成。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "过程执行 {ExecutionId} 的迟到事件重算失败，将由持久化队列退避重试", executionId);
            return ProcessExecutionAnalysisRecomputeAttempt.RetryableFailure(exception.Message);
        }
    }
}

internal sealed record ProcessExecutionAnalysisRecomputeAttempt(
    bool Succeeded,
    bool Terminal,
    string? Error)
{
    public static ProcessExecutionAnalysisRecomputeAttempt Success { get; } = new(true, false, null);

    public static ProcessExecutionAnalysisRecomputeAttempt RetryableFailure(string error)
        => new(false, false, error);

    public static ProcessExecutionAnalysisRecomputeAttempt TerminalFailure(string error)
        => new(false, true, error);
}

public sealed class ProcessExecutionAnalysisRecomputeOptions
{
    public int MaxAttempts { get; init; } = 8;
}
