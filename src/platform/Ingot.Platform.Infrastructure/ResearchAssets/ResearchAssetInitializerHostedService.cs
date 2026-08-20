// 在宿主启动阶段初始化 ResearchAsset 所需的持久化资源。

using Ingot.Platform.Application.ResearchAssets;
namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class ResearchAssetInitializerHostedService(
    IResearchAssetStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
