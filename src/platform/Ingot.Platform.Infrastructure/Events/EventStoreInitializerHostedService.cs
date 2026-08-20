// 在宿主启动阶段初始化 EventStore 所需的持久化资源。

using Ingot.Platform.Application.Events;

namespace Ingot.Platform.Infrastructure.Events;

public sealed class EventStoreInitializerHostedService(IPlatformEventStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
