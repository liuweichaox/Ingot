// 验证平台组件 PlatformAuthorization 的成功、拒绝和安全边界。

using System.Reflection;
using Ingot.Platform.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PlatformAuthorizationTests
{
    [Fact]
    public void AnonymousControllerActions_AreExplicitlyWhitelisted()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "AuthController.Configuration",
            "AuthController.Login",
            "IngestionTasksController.Active",
            "IngestionTasksController.NextProbeTask",
            "IngestionTasksController.CompleteProbeTask",
            "EdgesController.Register",
            "EdgesController.Heartbeat",
            "EventsController.Ingest"
        };

        var anonymous = typeof(AuthController).Assembly
            .GetTypes()
            .Where(static type => type.IsClass && !type.IsAbstract &&
                                 typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Where(static method => method.GetCustomAttribute<HttpMethodAttribute>() is not null)
            .Where(static method => method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(static method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(allowed.OrderBy(static value => value), anonymous.OrderBy(static value => value));
    }
}
