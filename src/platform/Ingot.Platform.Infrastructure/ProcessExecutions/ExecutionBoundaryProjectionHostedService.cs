using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>
///     从持久化重算队列生成运行边界。任务与事件在同一事务登记，API 重启不会丢失；
///     租约允许多个 Worker 副本安全竞争。
/// </summary>
public sealed class ExecutionBoundaryProjectionHostedService(
    PostgresExecutionBoundaryStore store,
    IOptions<ExecutionBoundaryProjectionOptions> options,
    ILogger<ExecutionBoundaryProjectionHostedService> logger) : BackgroundService
{
    private readonly ExecutionBoundaryProjectionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var lease = await store.ClaimProjectionAsync(_options.LeaseTimeout, stoppingToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var result = await store.ProjectAsync(
                        lease,
                        _options.ExecutionTimeoutWithoutCompletion,
                        stoppingToken)
                    .ConfigureAwait(false);
                if (!await store.FinishProjectionAsync(lease, result?.RecheckAt, stoppingToken)
                        .ConfigureAwait(false))
                {
                    logger.LogWarning(
                        "运行边界投影租约已失效：Site={SiteId}, ExecutionId={ExecutionId}",
                        lease.SiteId,
                        lease.SourceExecutionId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(
                    _options.MaximumRetryDelay.TotalSeconds,
                    Math.Pow(2, Math.Min(lease.AttemptCount, 10))));
                logger.LogError(
                    exception,
                    lease.AttemptCount >= _options.MaxAttempts
                        ? "运行边界投影达到最大尝试次数，转入失败终态：Site={SiteId}, ExecutionId={ExecutionId}"
                        : "运行边界投影失败，将退避重试：Site={SiteId}, ExecutionId={ExecutionId}",
                    lease.SiteId,
                    lease.SourceExecutionId);
                await store.RetryProjectionAsync(
                        lease,
                        exception.Message,
                        delay,
                        _options.MaxAttempts,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }
}

public sealed class ExecutionBoundaryProjectionOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ExecutionTimeoutWithoutCompletion { get; init; } = TimeSpan.FromHours(10);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxAttempts { get; init; } = 8;
}
