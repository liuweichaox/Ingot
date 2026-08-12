using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Events;

public sealed class EdgeDiagnosticsOptions
{
    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EdgeDiagnosticsTokenProvider(
    IOptions<EdgeDiagnosticsOptions> diagnosticsOptions,
    IOptions<PlatformEventOptions> eventOptions)
{
    private readonly EdgeDiagnosticsOptions _diagnosticsOptions = diagnosticsOptions.Value;
    private readonly PlatformEventOptions _eventOptions = eventOptions.Value;

    public bool TryGetToken(string edgeId, out string token)
    {
        if (_diagnosticsOptions.EdgeTokens.TryGetValue(edgeId, out var diagnosticsToken) &&
            !string.IsNullOrWhiteSpace(diagnosticsToken))
        {
            token = diagnosticsToken;
            return true;
        }

        // Preserve compatibility for nodes that still protect local diagnostics with
        // their event-ingest token. New deployments should configure a distinct token.
        if (_eventOptions.EdgeTokens.TryGetValue(edgeId, out var ingestToken) &&
            !string.IsNullOrWhiteSpace(ingestToken))
        {
            token = ingestToken;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
