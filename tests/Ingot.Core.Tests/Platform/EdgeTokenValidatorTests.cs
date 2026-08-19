using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EdgeTokenValidatorTests
{
    [Fact]
    public void IsAuthorized_ShouldRequireTokenBoundToEdge()
    {
        var validator = new EdgeTokenValidator(Options.Create(new PlatformEventOptions
        {
            RequireToken = true,
            EdgeTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-001"] = "secret-001"
            },
            EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-001"] = "SITE-001"
            }
        }));

        Assert.True(validator.IsAuthorized("site-001", "edge-001", "Bearer secret-001"));
        Assert.False(validator.IsAuthorized("SITE-002", "EDGE-001", "Bearer secret-001"));
        Assert.False(validator.IsAuthorized("SITE-001", "EDGE-001", "Bearer wrong"));
        Assert.False(validator.IsAuthorized("SITE-001", "EDGE-002", "Bearer secret-001"));
        Assert.False(validator.IsAuthorized("SITE-001", "EDGE-001", null));
    }
}
