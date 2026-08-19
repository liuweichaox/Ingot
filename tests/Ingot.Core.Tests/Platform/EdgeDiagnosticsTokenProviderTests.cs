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

    private static EdgeDiagnosticsTokenProvider CreateProvider(string? diagnosticsToken)
    {
        var diagnostics = new EdgeDiagnosticsOptions();
        if (diagnosticsToken is not null)
            diagnostics.EdgeTokens["EDGE-01"] = diagnosticsToken;

        return new EdgeDiagnosticsTokenProvider(Options.Create(diagnostics));
    }
}
