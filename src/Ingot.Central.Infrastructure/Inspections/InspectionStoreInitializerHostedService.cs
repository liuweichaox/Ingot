namespace Ingot.Central.Infrastructure.Inspections;

public sealed class InspectionStoreInitializerHostedService(
    IInspectionRecordStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

