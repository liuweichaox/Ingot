using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Events;

public sealed class EdgeDiagnosticsOptions
{
    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> EdgeBaseUrls { get; set; }
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

    public bool TryGetBaseUrl(string edgeId, out string baseUrl)
    {
        if (_diagnosticsOptions.EdgeBaseUrls.TryGetValue(edgeId, out var configured) &&
            Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment))
        {
            baseUrl = uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        baseUrl = string.Empty;
        return false;
    }
}
