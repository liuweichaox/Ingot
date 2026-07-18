namespace Ingot.Central.Infrastructure.Events;

public sealed class EventStoreInitializerHostedService(ICentralEventStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
