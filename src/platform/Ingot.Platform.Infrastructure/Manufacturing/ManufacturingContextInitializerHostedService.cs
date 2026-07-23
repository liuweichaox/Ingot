namespace Ingot.Platform.Infrastructure.Manufacturing;

public sealed class ManufacturingContextInitializerHostedService(IManufacturingContextStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
