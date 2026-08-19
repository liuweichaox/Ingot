using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Events;

public sealed class EdgeDiagnosticsOptions
{
    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EdgeDiagnosticsTokenProvider(IOptions<EdgeDiagnosticsOptions> diagnosticsOptions)
{
    private readonly EdgeDiagnosticsOptions _diagnosticsOptions = diagnosticsOptions.Value;

    public bool TryGetToken(string edgeId, out string token)
    {
        if (_diagnosticsOptions.EdgeTokens.TryGetValue(edgeId, out var diagnosticsToken) &&
            !string.IsNullOrWhiteSpace(diagnosticsToken))
        {
            token = diagnosticsToken;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
