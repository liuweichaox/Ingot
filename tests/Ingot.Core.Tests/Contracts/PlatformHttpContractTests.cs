// 验证共享契约 PlatformHttpContract 的合法输入、拒绝和兼容边界。

using System.Reflection;
using Ingot.Contracts.Events;
using Ingot.Platform.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class PlatformHttpContractTests
{
    [Fact]
    public void EventBatchRoute_ShouldBeSharedByEdgeAndPlatform()
    {
        var action = typeof(EventsController).GetMethod(nameof(EventsController.Ingest));
        var route = action!.GetCustomAttribute<HttpPostAttribute>();

        Assert.Equal(PlatformEventRoutes.AbsoluteBatchIngest, route!.Template);
        Assert.Equal("api/v1/events:batch", PlatformEventRoutes.BatchIngest);
    }
}
