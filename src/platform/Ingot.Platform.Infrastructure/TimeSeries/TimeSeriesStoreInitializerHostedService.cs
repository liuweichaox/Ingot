// 在宿主启动阶段初始化 TimeSeriesStore 所需的持久化资源。

namespace Ingot.Platform.Infrastructure.TimeSeries;

public sealed class TimeSeriesStoreInitializerHostedService(
    ITimeSeriesStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
