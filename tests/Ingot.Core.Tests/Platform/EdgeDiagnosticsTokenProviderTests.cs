using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EdgeDiagnosticsTokenProviderTests
{
    [Fact]
    public void TryGetToken_ShouldPreferDedicatedDiagnosticsToken()
    {
        var provider = CreateProvider("diagnostics-token", "ingest-token");

        Assert.True(provider.TryGetToken("EDGE-01", out var token));
        Assert.Equal("diagnostics-token", token);
    }

    [Fact]
    public void TryGetToken_ShouldFallBackToIngestTokenForLegacyNode()
    {
        var provider = CreateProvider(null, "ingest-token");

        Assert.True(provider.TryGetToken("EDGE-01", out var token));
        Assert.Equal("ingest-token", token);
    }

    private static EdgeDiagnosticsTokenProvider CreateProvider(
        string? diagnosticsToken,
        string? ingestToken)
    {
        var diagnostics = new EdgeDiagnosticsOptions();
        if (diagnosticsToken is not null)
            diagnostics.EdgeTokens["EDGE-01"] = diagnosticsToken;

        var events = new PlatformEventOptions();
        if (ingestToken is not null)
            events.EdgeTokens["EDGE-01"] = ingestToken;

        return new EdgeDiagnosticsTokenProvider(
            Options.Create(diagnostics),
            Options.Create(events));
    }
}
