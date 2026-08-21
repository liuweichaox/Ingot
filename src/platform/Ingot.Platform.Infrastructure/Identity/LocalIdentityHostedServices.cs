using Ingot.Platform.Application.Identity;

namespace Ingot.Platform.Infrastructure.Identity;

public sealed class SessionPruneHostedService(
    ILocalUserStore store,
    ILogger<SessionPruneHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pruned = await store.PruneExpiredSessionsAsync(stoppingToken).ConfigureAwait(false);
                if (pruned > 0)
                    logger.LogInformation("已清理 {Count} 条过期会话。", pruned);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "清理过期会话失败，下个周期重试。");
            }
            try
            {
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
