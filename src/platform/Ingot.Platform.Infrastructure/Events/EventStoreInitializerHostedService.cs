namespace Ingot.Platform.Infrastructure.Events;

public sealed class EventStoreInitializerHostedService(IPlatformEventStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
