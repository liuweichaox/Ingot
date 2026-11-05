// 验证边缘节点身份必须绑定明确的站点，即使未启用节点令牌。
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class AcquisitionSiteAuthorizationTests
{
    [Fact]
    public void EdgeSiteLookup_FailsClosedWithoutAnExplicitMappingEvenWhenTokensAreDisabled()
    {
        var validator = new EdgeTokenValidator(Options.Create(new PlatformEventOptions
        {
            RequireToken = false,
            EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-A"] = "SITE-A"
            }
        }));

        Assert.True(validator.TryGetSiteId("edge-a", out var siteId));
        Assert.Equal("SITE-A", siteId);
        Assert.False(validator.TryGetSiteId("EDGE-UNKNOWN", out _));
    }
}
