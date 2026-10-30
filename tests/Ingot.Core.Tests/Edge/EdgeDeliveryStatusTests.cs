// 验证 Edge 队列状态使用单调时钟生成观察年龄。

using Ingot.Edge.Application.Abstractions;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class EdgeDeliveryStatusTests
{
    [Fact]
    public void Get_ReportsBacklogObservationAgeFromMonotonicClock()
    {
        var status = new EdgeDeliveryStatus();
        status.RecordBacklog(4);

        var snapshot = status.Get();

        Assert.Equal(4, snapshot.PendingEventCount);
        Assert.NotNull(snapshot.BacklogObservationAgeMs);
        Assert.InRange(snapshot.BacklogObservationAgeMs!.Value, 0, 30_000);
    }
}
