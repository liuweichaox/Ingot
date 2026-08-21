
using System.Text;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class HttpAcquisitionRequestFactory
{
    public static Uri CreateEndpoint(string baseUrl, string snapshotPath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
            throw new InvalidOperationException("设备基础地址必须是不含凭据、查询或片段的 HTTP/HTTPS 绝对地址。");
        if (string.IsNullOrWhiteSpace(snapshotPath) ||
            Uri.TryCreate(snapshotPath, UriKind.Absolute, out var absolutePath) &&
            absolutePath.Scheme is "http" or "https" ||
            snapshotPath.StartsWith("//", StringComparison.Ordinal) ||
            snapshotPath.StartsWith("\\\\", StringComparison.Ordinal) ||
            snapshotPath.Contains('\r') || snapshotPath.Contains('\n'))
            throw new InvalidOperationException("设备快照路径必须是相对于基础地址的安全路径。");

        var endpoint = new Uri(
            $"{baseUri.AbsoluteUri.TrimEnd('/')}/{snapshotPath.TrimStart('/')}",
            UriKind.Absolute);
        if (!string.Equals(endpoint.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(endpoint.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Port != baseUri.Port)
            throw new InvalidOperationException("设备快照地址不能离开已配置的设备主机。");
        return endpoint;
    }

    public static HttpRequestMessage Create(
        Uri endpoint,
        string method,
        string? requestBody,
        string? contentType,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> headerSecretRefs,
        IAcquisitionSecretResolver secrets)
    {
        var request = new HttpRequestMessage(method == "post" ? HttpMethod.Post : HttpMethod.Get, endpoint);
        if (requestBody is not null)
            request.Content = new StringContent(requestBody, Encoding.UTF8, contentType ?? "application/json");
        try
        {
            foreach (var (name, value) in headers)
                AddHeader(request, name, value);
            foreach (var (name, secretRef) in headerSecretRefs)
                AddHeader(request, name, secrets.Resolve(secretRef) ??
                    throw new InvalidOperationException($"HTTP 请求头 {name} 引用的密钥不存在。"));
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static void AddHeader(HttpRequestMessage request, string name, string value)
    {
        if (!request.Headers.TryAddWithoutValidation(name, value) &&
            (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(name, value)))
            throw new InvalidOperationException($"HTTP 请求头 {name} 不能应用到设备请求。");
    }
}
