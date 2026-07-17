namespace Ingot.Central.Api.Events;

public sealed class EventStoreInitializerHostedService(ICentralEventStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
