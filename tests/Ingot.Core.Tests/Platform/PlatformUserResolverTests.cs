// 验证平台组件 PlatformUserResolver 的成功、拒绝和安全边界。

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
            new Claim(ClaimTypes.Role, "QUALITY.REVIEWER"),
            new Claim(PlatformClaimTypes.SiteId, "SITE-001")
        ], "test"));

        var identity = Assert.IsType<PlatformIdentity>(resolver.ResolveIdentity(principal));
        Assert.True(identity.HasAnyRole(PlatformRoles.QualityReviewer));
        Assert.False(identity.HasAnyRole(PlatformRoles.QualityInspector));
        Assert.True(identity.CanAccessSite("site-001"));
        Assert.False(identity.CanAccessSite("SITE-002"));
        Assert.Equal(
            SiteScopeFailure.None,
            PlatformSiteScope.Resolve(identity, "site-001", false, out var canonicalSiteId));
        Assert.Equal("SITE-001", canonicalSiteId);
    }

    [Fact]
    public void RawOidcClaimsUseConfiguredSubjectRoleAndSiteTypes()
    {
        var resolver = new PlatformUserResolver(Environment("Production"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "Oidc-Engineer-01"),
            new Claim("name", "OIDC Engineer"),
            new Claim("roles", "PROCESS.ENGINEER"),
            new Claim(PlatformClaimTypes.SiteId, "SITE-001")
        ], "oidc", "name", "roles"));

        var identity = Assert.IsType<PlatformIdentity>(resolver.ResolveIdentity(principal));
        Assert.Equal("oidc-engineer-01", identity.UserId);
        Assert.True(identity.HasAnyRole(PlatformRoles.ProcessEngineer));
        Assert.True(identity.CanAccessSite("SITE-001"));
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
