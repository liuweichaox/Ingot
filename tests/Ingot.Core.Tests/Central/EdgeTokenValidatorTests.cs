using Ingot.Central.Api.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Central;

public sealed class EdgeTokenValidatorTests
{
    [Fact]
    public void IsAuthorized_ShouldRequireTokenBoundToEdge()
    {
        var validator = new EdgeTokenValidator(Options.Create(new CentralEventOptions
        {
            RequireToken = true,
            EdgeTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-001"] = "secret-001"
            }
        }));

        Assert.True(validator.IsAuthorized("edge-001", "Bearer secret-001"));
        Assert.False(validator.IsAuthorized("EDGE-001", "Bearer wrong"));
        Assert.False(validator.IsAuthorized("EDGE-002", "Bearer secret-001"));
        Assert.False(validator.IsAuthorized("EDGE-001", null));
    }
}
