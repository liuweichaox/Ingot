// 在宿主启动阶段初始化 AgentRunStore 所需的持久化资源。

using Microsoft.Extensions.Hosting;

namespace Ingot.Agent.Providers;

public sealed class AgentRunStoreInitializerHostedService(IAgentRunStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
