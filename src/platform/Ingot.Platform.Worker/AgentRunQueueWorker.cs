// 在独立 Platform Worker 中持续刷新模型配置并执行带租约的 Chat 运行。
using Ingot.Agent;

namespace Ingot.Platform.Worker;

public sealed class AgentRunQueueWorker(
    IAgentRunProcessor processor,
    IModelServiceConfigurationProvider modelSettings,
    ILogger<AgentRunQueueWorker> logger) : BackgroundService
{
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Guid.CreateVersion7()}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextConfigurationRefresh = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextConfigurationRefresh)
                {
                    await modelSettings.RefreshAsync(stoppingToken).ConfigureAwait(false);
                    nextConfigurationRefresh = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                if (!await processor.ProcessNextAsync(_leaseOwner, stoppingToken).ConfigureAwait(false))
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "持久 Chat 运行处理失败，Worker 将继续领取后续任务。");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
