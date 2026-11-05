// 定期向平台报告节点身份和上报队列状态。
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.BackgroundServices;

public sealed class EdgePlatformReporterHostedService(
    IPlatformReportingClient client,
    EdgeDeliveryStatus deliveryStatus,
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
            await client.SendHeartbeatAsync(
                null,
                deliveryStatus.Get(),
                stoppingToken).ConfigureAwait(false);
        }
    }
}
