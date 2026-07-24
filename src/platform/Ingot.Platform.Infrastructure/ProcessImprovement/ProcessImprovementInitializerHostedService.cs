namespace Ingot.Platform.Infrastructure.ProcessImprovement;

public sealed class ProcessImprovementInitializerHostedService(
    IProcessImprovementStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
