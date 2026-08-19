using Ingot.Platform.Api.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EdgeDiagnosticsTokenProviderTests
{
    [Fact]
    public void TryGetToken_ShouldPreferDedicatedDiagnosticsToken()
    {
        var provider = CreateProvider("diagnostics-token");

        Assert.True(provider.TryGetToken("EDGE-01", out var token));
        Assert.Equal("diagnostics-token", token);
    }

    [Fact]
    public void TryGetToken_ShouldRejectMissingDedicatedDiagnosticsToken()
    {
        var provider = CreateProvider(null);

        Assert.False(provider.TryGetToken("EDGE-01", out var token));
        Assert.Empty(token);
    }

    [Fact]
    public void TryGetBaseUrl_ShouldUseOnlyPinnedSafeTarget()
    {
        var options = new EdgeDiagnosticsOptions();
        options.EdgeBaseUrls["EDGE-01"] = "http://edge-01:8001/";
        var provider = new EdgeDiagnosticsTokenProvider(Options.Create(options));

        Assert.True(provider.TryGetBaseUrl("EDGE-01", out var baseUrl));
        Assert.Equal("http://edge-01:8001", baseUrl);
        Assert.False(provider.TryGetBaseUrl("UNREGISTERED", out _));

        options.EdgeBaseUrls["BAD"] = "http://user:secret@attacker.invalid/path?redirect=1";
        provider = new EdgeDiagnosticsTokenProvider(Options.Create(options));
        Assert.False(provider.TryGetBaseUrl("BAD", out _));
    }

    private static EdgeDiagnosticsTokenProvider CreateProvider(string? diagnosticsToken)
    {
        var diagnostics = new EdgeDiagnosticsOptions();
        if (diagnosticsToken is not null)
            diagnostics.EdgeTokens["EDGE-01"] = diagnosticsToken;

        return new EdgeDiagnosticsTokenProvider(Options.Create(diagnostics));
    }
}
