using System.Text.Json;
using Ingot.Application.Abstractions;
using Ingot.Contracts.Edge;
using Ingot.Edge.Agent.Services;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.Agent.BackgroundServices;

/// <summary>
/// Edge 启动即自动注册到中心，并周期发送心跳（在线状态、错误摘要等）。
/// </summary>
public sealed class EdgeCentralReporterHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EdgeIdentityService _identity;
    private readonly ILogger<EdgeCentralReporterHostedService> _logger;
    private readonly EdgeReportingOptions _options;
    private readonly IConfiguration _configuration;

    private string? _lastError;
    private string? _agentBaseUrl;

    public EdgeCentralReporterHostedService(
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeReportingOptions> options,
        EdgeIdentityService identity,
        IConfiguration configuration,
        ILogger<EdgeCentralReporterHostedService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _identity = identity;
        _configuration = configuration;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableCentralReporting)
        {
            _logger.LogInformation("已禁用中心上报（Edge:EnableCentralReporting=false）");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.CentralApiBaseUrl))
        {
            _logger.LogWarning("CentralApiBaseUrl 为空，跳过中心上报");
            return;
        }

        string edgeId;
        try
        {
            edgeId = _identity.GetEdgeId();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法获取 EdgeId，已跳过中心上报");
            return;
        }
        var hostname = Environment.MachineName;

        // 获取 Edge Agent 的 URL（用于中心代理访问）
        var urls = _configuration["Urls"] ?? _configuration["ASPNETCORE_URLS"] ?? "http://localhost:8001";
        var firstUrl = urls.Split(';', ',').FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(firstUrl))
        {
            // +/*/0.0.0.0 是 ASP.NET 通配符，不是合法 URI 主机名，需要提取 scheme 和 port 后替换
            var needReplace = firstUrl.Contains("://+:") || firstUrl.Contains("://*:")
                              || firstUrl.Contains("://0.0.0.0:") || firstUrl.Contains("://localhost:");
            if (needReplace)
            {
                // 提取端口号（最后一个 : 之后的数字）
                var lastColon = firstUrl.LastIndexOf(':');
                var port = firstUrl[(lastColon + 1)..];
                var scheme = firstUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";

                var realIp = GetLocalIpAddress() ?? "localhost";
                firstUrl = $"{scheme}://{realIp}:{port}";
            }
            _agentBaseUrl = firstUrl.TrimEnd('/');
        }

        var baseUri = new Uri(_options.CentralApiBaseUrl.TrimEnd('/') + "/");
        var http = _httpClientFactory.CreateClient(nameof(EdgeCentralReporterHostedService));
        http.BaseAddress = baseUri;

        _logger.LogInformation("中心上报启用：EdgeId={EdgeId}, Central={Central}, AgentBaseUrl={AgentBaseUrl}",
            edgeId, baseUri, _agentBaseUrl);

        // 启动即注册：如果中心暂不可用，则持续重试直到成功（或进程退出）
        await RegisterWithRetryAsync(http, edgeId, hostname, stoppingToken).ConfigureAwait(false);

        var heartbeatSeconds = _options.HeartbeatIntervalSeconds <= 0 ? 10 : _options.HeartbeatIntervalSeconds;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(heartbeatSeconds));

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await SendHeartbeatAsync(http, edgeId, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RegisterWithRetryAsync(HttpClient http, string edgeId, string hostname,
        CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var req = new EdgeRegistrationRequest
                {
                    EdgeId = edgeId,
                    AgentBaseUrl = _agentBaseUrl,
                    Hostname = hostname
                };

                using var resp = await http.PostAsJsonAsync("api/edges/register", req, JsonOptions, ct)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                _lastError = null;
                _logger.LogInformation("已向中心注册/更新：EdgeId={EdgeId}", edgeId);
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

    private async Task SendHeartbeatAsync(HttpClient http, string edgeId, CancellationToken ct)
    {
        try
        {
            var req = new EdgeHeartbeatRequest
            {
                EdgeId = edgeId,
                AgentBaseUrl = _agentBaseUrl,
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
    ///     获取本机第一个可用的真实 IPv4 地址
    /// </summary>
    private static string? GetLocalIpAddress()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            // 连接一个外部地址（不会真正发包），获取本机出口 IP
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint)
                return endPoint.Address.ToString();
        }
        catch
        {
            // ignored
        }
        return null;
    }
}
