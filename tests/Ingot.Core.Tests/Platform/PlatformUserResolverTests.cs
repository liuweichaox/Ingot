using System.Security.Claims;
using Ingot.Platform.Api.Agents;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PlatformUserResolverTests
{
    [Fact]
    public void AuthenticatedPlatformIdentityIsUsed()
    {
        var resolver = new PlatformUserResolver(Environment("Production"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "Analyst-01")
        ], "test"));

        Assert.Equal("analyst-01", resolver.Resolve(principal));
    }

    [Fact]
    public void AuthenticatedRolesAreNormalizedAndServerOwned()
    {
        var resolver = new PlatformUserResolver(Environment("Production"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "Reviewer-01"),
            new Claim(ClaimTypes.Role, "QUALITY.REVIEWER")
        ], "test"));

        var identity = Assert.IsType<PlatformIdentity>(resolver.ResolveIdentity(principal));
        Assert.True(identity.HasAnyRole(PlatformRoles.QualityReviewer));
        Assert.False(identity.HasAnyRole(PlatformRoles.QualityInspector));
    }

    [Fact]
    public void DevelopmentUsesServerOwnedLocalIdentity()
    {
        var resolver = new PlatformUserResolver(Environment("Development"));

        Assert.Equal("operator", resolver.Resolve(new ClaimsPrincipal()));
    }

    [Fact]
    public void ProductionRejectsMissingPlatformIdentity()
    {
        var resolver = new PlatformUserResolver(Environment("Production"));

        Assert.Null(resolver.Resolve(new ClaimsPrincipal()));
    }

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment
    {
        EnvironmentName = name
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
