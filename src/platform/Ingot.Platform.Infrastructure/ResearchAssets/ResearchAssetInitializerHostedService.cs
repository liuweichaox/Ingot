
using Ingot.Platform.Application.ResearchAssets;
namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class ResearchAssetInitializerHostedService(
    IResearchAssetStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
