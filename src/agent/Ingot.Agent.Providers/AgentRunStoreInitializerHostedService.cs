using Microsoft.Extensions.Hosting;

namespace Ingot.Agent.Providers;

public sealed class AgentRunStoreInitializerHostedService(IAgentRunStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
