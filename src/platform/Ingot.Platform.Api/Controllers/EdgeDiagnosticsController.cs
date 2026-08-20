using Ingot.Platform.Infrastructure.Services;
using Ingot.Platform.Api.Events;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

/// <summary>
/// 中心代理：按 edgeId 代理查询 Edge ConnectorHost 的诊断数据（metrics/logs）。
/// 说明：Platform.Api 仍然是纯 API，不提供 UI。
/// </summary>
[ApiController]
[Route("api/edges/{edgeId}")]
public sealed class EdgeDiagnosticsController(
    EdgeRegistry registry,
    EdgeDiagnosticsTokenProvider diagnosticsTokenProvider,
    IHttpClientFactory httpClientFactory) : PlatformApiController
{
    [HttpGet("metrics/raw")]
    public async Task<IActionResult> GetEdgeMetricsRaw([FromRoute] string edgeId, CancellationToken cancellationToken)
    {
        var reported = registry.Find(edgeId)?.Acquisition;
        if (reported is not null)
            return Ok(reported);

        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return InvalidRequest("该采集节点未配置可信诊断地址，无法代理 metrics。");

        var uri = new Uri(new Uri(baseUrl), "/metrics");
        var client = CreateEdgeClient(edgeId);

        using var resp = await client.GetAsync(uri, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return EdgeProxyFailure(resp, body);

        return Content(body, "text/plain; version=0.0.4; charset=utf-8");
    }

    [HttpGet("metrics/json")]
    public async Task<IActionResult> GetEdgeMetricsJson([FromRoute] string edgeId, CancellationToken cancellationToken)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return InvalidRequest("该采集节点未配置可信诊断地址，无法代理 metrics。");

        var uri = new Uri(new Uri(baseUrl), "/metrics");
        var client = CreateEdgeClient(edgeId);

        using var resp = await client.GetAsync(uri, cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return EdgeProxyFailure(resp, text);

        var metrics = PrometheusTextParser.Parse(text);
        return Ok(new
        {
            edgeId,
            timestamp = DateTimeOffset.UtcNow,
            metrics
        });
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetEdgeLogs(
        [FromRoute] string edgeId,
        [FromQuery] string? level = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? audience = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return InvalidRequest("该采集节点未配置可信诊断地址，无法代理 logs。");

        var query = new Dictionary<string, string?>
        {
            ["level"] = string.IsNullOrWhiteSpace(level) ? null : level,
            ["keyword"] = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            ["audience"] = string.IsNullOrWhiteSpace(audience) ? null : audience,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };

        var qs = string.Join("&", query.Where(kv => kv.Value != null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        var path = string.IsNullOrWhiteSpace(qs) ? "/api/logs" : $"/api/logs?{qs}";
        var uri = new Uri(new Uri(baseUrl), path);

        var client = CreateEdgeClient(edgeId);
        try
        {
            using var resp = await client.GetAsync(uri, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode) return EdgeProxyFailure(resp, body);

            // 透传 edge 返回的 JSON（保持字段命名一致）
            return Content(body, "application/json; charset=utf-8");
        }
        catch (HttpRequestException exception)
        {
            return ProblemResponse(
                StatusCodes.Status502BadGateway,
                "采集节点不可访问，请检查节点网络或上报地址。",
                [("upstreamDetail", exception.Message)]);
        }
    }

    [HttpGet("logs/levels")]
    public async Task<IActionResult> GetEdgeLogLevels([FromRoute] string edgeId, CancellationToken cancellationToken)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return InvalidRequest("该采集节点未配置可信诊断地址，无法代理 logs。");

        var uri = new Uri(new Uri(baseUrl), "/api/logs/levels");
        var client = CreateEdgeClient(edgeId);
        using var resp = await client.GetAsync(uri, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return EdgeProxyFailure(resp, body);

        return Content(body, "application/json; charset=utf-8");
    }

    [HttpGet("acquisition/status")]
    public async Task<IActionResult> GetAcquisitionStatus(
        [FromRoute] string edgeId,
        CancellationToken cancellationToken)
    {
        var reported = registry.Find(edgeId)?.Acquisition;
        if (reported is not null)
            return Ok(reported);

        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl is null)
            return InvalidRequest("该采集节点未配置可信诊断地址，无法查询任务状态。");

        var uri = new Uri(new Uri(baseUrl), "/api/v1/acquisition/status");
        var client = CreateEdgeClient(edgeId);
        try
        {
            using var response = await client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? Content(body, "application/json; charset=utf-8")
                : EdgeProxyFailure(response, body);
        }
        catch (HttpRequestException exception)
        {
            return ProblemResponse(
                StatusCodes.Status502BadGateway,
                "采集节点不可访问。",
                [("upstreamDetail", exception.Message)]);
        }
    }

    [HttpGet("delivery/status")]
    public IActionResult GetDeliveryStatus([FromRoute] string edgeId)
    {
        var state = registry.Find(edgeId);
        if (state is null)
            return ResourceNotFound("采集节点不存在。");
        return state.Delivery is null
            ? ResourceNotFound("采集节点尚未上报数据上送状态。")
            : Ok(state.Delivery);
    }

    private string? GetEdgeBaseUrlOrNull(string edgeId)
    {
        return diagnosticsTokenProvider.TryGetBaseUrl(edgeId, out var baseUrl)
            ? baseUrl
            : null;
    }

    private HttpClient CreateEdgeClient(string edgeId)
    {
        var client = httpClientFactory.CreateClient("edge-diagnostics");
        if (diagnosticsTokenProvider.TryGetToken(edgeId, out var token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private IActionResult EdgeProxyFailure(HttpResponseMessage response, string body)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return ProblemResponse(
                StatusCodes.Status502BadGateway,
                "平台无法通过节点诊断凭据访问该采集节点，请检查节点凭据配置。",
                [("edgeStatus", (int)response.StatusCode)]);
        }

        return ProblemResponse((int)response.StatusCode, body, []);
    }
}
