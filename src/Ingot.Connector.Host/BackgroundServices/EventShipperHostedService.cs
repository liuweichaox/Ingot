using Ingot.Application.Abstractions;

namespace Ingot.Connector.Host.BackgroundServices;

public sealed class EventShipperHostedService(IEventShipper shipper) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await shipper.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停机。
        }
    }
}
