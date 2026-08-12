using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class HttpAcquisitionRequestFactoryTests
{
    [Fact]
    public void EndpointResolutionIsStableForBasePathsAndLeadingSlash()
    {
        var endpoint = HttpAcquisitionRequestFactory.CreateEndpoint(
            "https://device.local/api/v1", "/snapshot?line=1");

        Assert.Equal("https://device.local/api/v1/snapshot?line=1", endpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://other.invalid/snapshot")]
    [InlineData("//other.invalid/snapshot")]
    [InlineData("\\\\other.invalid\\snapshot")]
    public void EndpointCannotOverrideConfiguredAuthority(string path)
        => Assert.Throws<InvalidOperationException>(() =>
            HttpAcquisitionRequestFactory.CreateEndpoint("https://device.local", path));
}
