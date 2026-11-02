// Verify Edge log audience filtering across platform role boundaries.
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EdgeLogAudiencePolicyTests
{
    [Theory]
    [InlineData(PlatformRoles.QualityInspector)]
    [InlineData(PlatformRoles.QualityReviewer)]
    [InlineData(PlatformRoles.ProcessEngineer)]
    public void BusinessRoles_AreRestrictedToOperatorLogs_WhenAudienceIsMissingOrSystem(string role)
    {
        var identity = Identity(role);

        Assert.Equal("operator", EdgeLogAudiencePolicy.Resolve(identity, null));
        Assert.Equal("operator", EdgeLogAudiencePolicy.Resolve(identity, "system"));
        Assert.Equal("operator", EdgeLogAudiencePolicy.Resolve(identity, "operator"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("system", "system")]
    [InlineData("operator", "operator")]
    public void Administrator_CanRequestAnyLogAudience(string? requestedAudience, string? expectedAudience)
    {
        var identity = Identity(PlatformRoles.PlatformAdministrator);

        Assert.Equal(expectedAudience, EdgeLogAudiencePolicy.Resolve(identity, requestedAudience));
    }

    private static PlatformIdentity Identity(string role)
        => new(
            "user-1",
            new HashSet<string>([role], StringComparer.Ordinal),
            new HashSet<string>(["SITE-A"], StringComparer.OrdinalIgnoreCase));
}
