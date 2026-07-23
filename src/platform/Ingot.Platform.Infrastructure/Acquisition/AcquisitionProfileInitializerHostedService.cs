namespace Ingot.Platform.Infrastructure.Acquisition;

public sealed class AcquisitionProfileInitializerHostedService(IAcquisitionProfileStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
