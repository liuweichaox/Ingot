namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleAnalysisMaterializationInitializerHostedService(
    ICycleAnalysisMaterializationStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
