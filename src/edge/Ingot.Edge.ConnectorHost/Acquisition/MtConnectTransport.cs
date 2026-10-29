// Pins read-only MTConnect requests through the Edge egress policy and never follows redirects.
using System.Net.Http.Headers;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal sealed class MtConnectTransport
{
    private const int MaximumEncodedPathQueryLength = 8 * 1024;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAcquisitionSecretResolver _secrets;
    private readonly AcquisitionHttpEgressPolicy _egressPolicy;

    public MtConnectTransport(
        IHttpClientFactory httpClientFactory,
        IAcquisitionSecretResolver secrets,
        AcquisitionHttpEgressPolicy egressPolicy)
    {
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
        _egressPolicy = egressPolicy;
    }

    public async Task<MtConnectProbeDocument> ReadProbeAsync(
        MtConnectConnection connection,
        CancellationToken ct)
    {
        var endpoint = CreateEndpoint(connection, "probe");
        using var response = await SendGetAsync(connection, endpoint, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await MtConnectXmlReader.ReadProbeAsync(
            response.Content,
            connection.DeviceNameOrUuid,
            connection.MaximumResponseBytes,
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, MtConnectObservation>> ReadCurrentAsync(
        MtConnectConnection connection,
        MtConnectProbeDocument catalog,
        IReadOnlyCollection<string> configuredIds,
        CancellationToken ct)
    {
        var pathExpression = CreateDataItemPathExpression(configuredIds);
        var endpointPath = $"current?path={Uri.EscapeDataString(pathExpression)}";
        if (endpointPath.Length > MaximumEncodedPathQueryLength)
            throw new InvalidOperationException(
                $"MTConnect 已配置点位生成的 /current path 查询超过 {MaximumEncodedPathQueryLength} 字符上限，请拆分采集任务。");
        var endpoint = CreateEndpoint(connection, endpointPath);
        using var response = await SendGetAsync(connection, endpoint, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await MtConnectXmlReader.ReadCurrentAsync(
            response.Content,
            catalog.DataItems,
            configuredIds.ToHashSet(StringComparer.Ordinal),
            connection.MaximumResponseBytes,
            ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        MtConnectConnection connection,
        Uri endpoint,
        CancellationToken ct)
    {
        if (connection.Headers.Keys.Concat(connection.HeaderSecretRefs.Keys)
            .Any(static name => string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("MTConnect Accept 请求头固定为 application/xml，不能在连接配置中覆盖。");
        await _egressPolicy.EnsureAllowedAsync(
            endpoint,
            ct,
            connection.HeaderSecretRefs.Count > 0).ConfigureAwait(false);
        using var request = HttpAcquisitionRequestFactory.Create(
            endpoint,
            "get",
            requestBody: null,
            contentType: null,
            connection.Headers,
            connection.HeaderSecretRefs,
            _secrets);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        return await _httpClientFactory.CreateClient("device-http-acquisition")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }

    private static Uri CreateEndpoint(MtConnectConnection connection, string relativePath)
    {
        var device = connection.DeviceNameOrUuid?.Trim();
        if (string.IsNullOrWhiteSpace(device) ||
            device is "." or ".." ||
            device.Contains('/') || device.Contains('\\') ||
            device.Any(char.IsControl))
            throw new InvalidOperationException("MTConnect 设备名称或 UUID 必须是单一安全路径段。");
        var route = $"{Uri.EscapeDataString(device)}/{relativePath}";
        return HttpAcquisitionRequestFactory.CreateEndpoint(connection.BaseUrl, route);
    }

    private static string CreateDataItemPathExpression(IReadOnlyCollection<string> configuredIds)
    {
        if (configuredIds.Count is < 1 or > MtConnectAcquisitionSupport.MaximumConfiguredDataItemIds)
            throw new InvalidOperationException(
                $"MTConnect 每个采集任务必须配置 1-{MtConnectAcquisitionSupport.MaximumConfiguredDataItemIds} 个 DataItem ID。");
        var predicates = configuredIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => $"@id={XPathLiteral(id)}")
            .ToArray();
        if (predicates.Length == 0)
            throw new InvalidOperationException("MTConnect 查询没有可读取的 DataItem ID。");
        return $"//DataItem[{string.Join(" or ", predicates)}]";
    }

    private static string XPathLiteral(string value)
    {
        if (!value.Contains('\'')) return $"'{value}'";
        if (!value.Contains('"')) return $"\"{value}\"";
        var pieces = value.Split('\'');
        return $"concat({string.Join(", \"'\", ", pieces.Select(piece => $"'{piece}'"))})";
    }
}
