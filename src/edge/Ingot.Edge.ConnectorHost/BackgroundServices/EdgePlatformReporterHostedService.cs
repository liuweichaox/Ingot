using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.BackgroundServices;

/// <summary>
/// Edge 启动即注册到中心并周期发送心跳。
/// 宿主只保留节拍循环，HTTP 客户端与重试逻辑见 IPlatformReportingClient 实现。
/// </summary>
public sealed class EdgePlatformReporterHostedService(
    IPlatformReportingClient client,
    IConfiguration configuration,
    ILogger<EdgePlatformReporterHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var urls = configuration["Urls"]
            ?? throw new InvalidOperationException("Urls is required.");
        if (!client.TryInitialize(urls))
        {
            logger.LogDebug("中心上报未启用或配置不完整，上报循环退出。");
            return;
        }

        await client.RegisterWithRetryAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(client.HeartbeatIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await client.SendHeartbeatAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
