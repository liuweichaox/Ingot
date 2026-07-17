using Ingot.Central.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

/// <summary>
/// 中心代理：按 edgeId 代理查询 Edge.Agent 的诊断数据（metrics/logs）。
/// 说明：Central.Api 仍然是纯 API，不提供 UI。
/// </summary>
[ApiController]
[Route("api/edges/{edgeId}")]
public sealed class EdgeDiagnosticsController(EdgeRegistry registry, IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpGet("metrics/raw")]
    public async Task<IActionResult> GetEdgeMetricsRaw([FromRoute] string edgeId, CancellationToken cancellationToken)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return BadRequest(new { error = "该 edge 未上报 AgentBaseUrl，无法代理 metrics。" });

        var uri = new Uri(new Uri(baseUrl), "/metrics");
        var client = httpClientFactory.CreateClient();

        using var resp = await client.GetAsync(uri, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

        return Content(body, "text/plain; version=0.0.4; charset=utf-8");
    }

    [HttpGet("metrics/json")]
    public async Task<IActionResult> GetEdgeMetricsJson([FromRoute] string edgeId, CancellationToken cancellationToken)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return BadRequest(new { error = "该 edge 未上报 AgentBaseUrl，无法代理 metrics。" });

        var uri = new Uri(new Uri(baseUrl), "/metrics");
        var client = httpClientFactory.CreateClient();

        using var resp = await client.GetAsync(uri, cancellationToken);
        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, text);

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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return BadRequest(new { error = "该 edge 未上报 AgentBaseUrl，无法代理 logs。" });

        var query = new Dictionary<string, string?>
        {
            ["level"] = string.IsNullOrWhiteSpace(level) ? null : level,
            ["keyword"] = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };

        var qs = string.Join("&", query.Where(kv => kv.Value != null)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}"));

        var path = string.IsNullOrWhiteSpace(qs) ? "/api/logs" : $"/api/logs?{qs}";
        var uri = new Uri(new Uri(baseUrl), path);

        var client = httpClientFactory.CreateClient();
        using var resp = await client.GetAsync(uri, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

        // 透传 edge 返回的 JSON（保持字段命名一致）
        return Content(body, "application/json; charset=utf-8");
    }

    [HttpGet("logs/levels")]
    public async Task<IActionResult> GetEdgeLogLevels([FromRoute] string edgeId, CancellationToken cancellationToken)
    {
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return BadRequest(new { error = "该 edge 未上报 AgentBaseUrl，无法代理 logs。" });

        var uri = new Uri(new Uri(baseUrl), "/api/logs/levels");
        var client = httpClientFactory.CreateClient();
        using var resp = await client.GetAsync(uri, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

        return Content(body, "application/json; charset=utf-8");
    }

    private string? GetEdgeBaseUrlOrNull(string edgeId)
    {
        var state = registry.Find(edgeId);
        var baseUrl = state?.AgentBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        return baseUrl;
    }
}
