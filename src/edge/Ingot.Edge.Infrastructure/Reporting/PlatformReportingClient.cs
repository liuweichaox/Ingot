using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Ingot.Contracts.Edge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.Infrastructure.Reporting;

/// <summary>
///     Connector Host 向 Platform 注册并发送心跳；注册使用 1 到 30 秒指数退避。
/// </summary>
public sealed class PlatformReportingClient : IPlatformReportingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEdgeIdentityProvider _identity;
    private readonly ILogger<PlatformReportingClient> _logger;
    private readonly EdgeReportingOptions _options;

    private HttpClient? _http;
    private string? _edgeId;
    private string? _hostname;
    private string? _hostBaseUrl;
    private string? _lastError;

    public PlatformReportingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeReportingOptions> options,
        IEdgeIdentityProvider identity,
        ILogger<PlatformReportingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identity = identity;
        _logger = logger;
        _options = options.Value;
    }

    public int HeartbeatIntervalSeconds =>
        _options.HeartbeatIntervalSeconds <= 0 ? 10 : _options.HeartbeatIntervalSeconds;

    public bool TryInitialize(string? listenUrls)
    {
        if (!_options.IsPlatformReportingEnabled)
        {
            _logger.LogInformation("已禁用 Platform 上报（Edge:EnablePlatformReporting=false）");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.EffectivePlatformApiBaseUrl))
        {
            _logger.LogWarning("PlatformApiBaseUrl 为空，跳过 Platform 上报");
            return false;
        }

        try
        {
            _edgeId = _identity.GetEdgeId();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法获取 EdgeId，已跳过中心上报");
            return false;
        }

        _hostname = Environment.MachineName;
        _hostBaseUrl = string.IsNullOrWhiteSpace(_options.PublicBaseUrl)
            ? NormalizeHostBaseUrl(listenUrls)
            : NormalizeConfiguredBaseUrl(_options.PublicBaseUrl);

        var baseUri = new Uri(_options.EffectivePlatformApiBaseUrl.TrimEnd('/') + "/");
        _http = _httpClientFactory.CreateClient(nameof(PlatformReportingClient));
        _http.BaseAddress = baseUri;
        if (!string.IsNullOrWhiteSpace(_options.EventIngestToken))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.EventIngestToken);

        _logger.LogInformation("Platform 上报启用：EdgeId={EdgeId}, Platform={Platform}, HostBaseUrl={HostBaseUrl}",
            _edgeId, baseUri, _hostBaseUrl);
        return true;
    }

    public async Task RegisterWithRetryAsync(CancellationToken ct = default)
    {
        var http = _http ?? throw new InvalidOperationException("必须先调用 TryInitialize。");
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var req = new EdgeRegistrationRequest
                {
                    EdgeId = _edgeId!,
                    HostBaseUrl = _hostBaseUrl,
                    Hostname = _hostname
                };

                using var resp = await http.PostAsJsonAsync("api/edges/register", req, JsonOptions, ct)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                _lastError = null;
                _logger.LogInformation("已向中心注册/更新：EdgeId={EdgeId}", _edgeId);
                return;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger.LogWarning(ex, "中心注册失败（将重试）：{Message}", ex.Message);

                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds, delay.TotalSeconds * 2));
            }
        }
    }

    public async Task SendHeartbeatAsync(CancellationToken ct = default)
    {
        var http = _http ?? throw new InvalidOperationException("必须先调用 TryInitialize。");
        try
        {
            var req = new EdgeHeartbeatRequest
            {
                EdgeId = _edgeId!,
                HostBaseUrl = _hostBaseUrl,
                LastError = _lastError,
                Timestamp = DateTimeOffset.UtcNow
            };

            using var resp = await http.PostAsJsonAsync("api/edges/heartbeat", req, JsonOptions, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning(ex, "中心心跳失败：{Message}", ex.Message);
        }
    }

    /// <summary>
    ///     +/*/0.0.0.0/localhost 是 ASP.NET 通配符或回环，不是中心可回访的主机名；
    ///     提取 scheme 与端口后用本机出口 IP 替换。
    /// </summary>
    private static string? NormalizeHostBaseUrl(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
            return null;

        var firstUrl = urls.Split(';', ',').FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstUrl))
            return null;

        var needReplace = firstUrl.Contains("://+:") || firstUrl.Contains("://*:")
                          || firstUrl.Contains("://0.0.0.0:") || firstUrl.Contains("://localhost:");
        if (needReplace)
        {
            var lastColon = firstUrl.LastIndexOf(':');
            var port = firstUrl[(lastColon + 1)..];
            var scheme = firstUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";

            var realIp = GetLocalIpAddress();
            if (realIp is null)
                return null;
            firstUrl = $"{scheme}://{realIp}:{port}";
        }

        return firstUrl.TrimEnd('/');
    }

    private static string NormalizeConfiguredBaseUrl(string value)
    {
        var normalized = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Edge:PublicBaseUrl 必须是 HTTP 或 HTTPS 绝对地址。");
        }

        return normalized;
    }

    /// <summary>获取本机第一个可用的真实 IPv4 出口地址。</summary>
    private static string? GetLocalIpAddress()
    {
        return System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(static network =>
                network.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                network.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(static network => network.GetIPProperties().UnicastAddresses)
            .Select(static address => address.Address)
            .FirstOrDefault(static address =>
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                !System.Net.IPAddress.IsLoopback(address))
            ?.ToString();
    }
}
