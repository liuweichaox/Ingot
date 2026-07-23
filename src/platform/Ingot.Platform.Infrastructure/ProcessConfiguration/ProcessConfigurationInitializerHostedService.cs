namespace Ingot.Platform.Infrastructure.ProcessConfiguration;

public sealed class ProcessConfigurationInitializerHostedService(IProcessConfigurationStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
