// 在站点授权边界内代理边缘节点诊断信息。
using System.Net.Http.Headers;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/edges/{edgeId}")]
public sealed class EdgeDiagnosticsController(
    EdgeRegistry registry,
    EdgeDiagnosticsTokenProvider diagnosticsTokenProvider,
    IHttpClientFactory httpClientFactory,
    PlatformUserResolver userResolver) : PlatformApiController
{
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
        var access = await ResolveEdgeAccessAsync(edgeId, cancellationToken).ConfigureAwait(false);
        if (access.Denial is not null) return access.Denial;
        var baseUrl = GetEdgeBaseUrlOrNull(edgeId);
        if (baseUrl == null) return InvalidRequest("该现场节点未配置可信诊断地址，无法代理 logs。");

        var query = new Dictionary<string, string?>
        {
            ["level"] = string.IsNullOrWhiteSpace(level) ? null : level,
            ["keyword"] = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            ["audience"] = EdgeLogAudiencePolicy.Resolve(access.Identity!, audience),
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

            return Content(body, "application/json; charset=utf-8");
        }
        catch (HttpRequestException exception)
        {
            return ProblemResponse(
                StatusCodes.Status502BadGateway,
                "现场节点不可访问，请检查节点网络或上报地址。",
                [("upstreamDetail", exception.Message)]);
        }
    }

    private string? GetEdgeBaseUrlOrNull(string edgeId)
    {
        return diagnosticsTokenProvider.TryGetBaseUrl(edgeId, out var baseUrl)
            ? baseUrl
            : null;
    }

    private async Task<(IActionResult? Denial, PlatformIdentity? Identity)> ResolveEdgeAccessAsync(
        string edgeId,
        CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return (AuthenticationRequired("需要平台统一认证。"), null);
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return (AuthorizationDenied(), null);
        var edge = await registry.FindAsync(edgeId, ct).ConfigureAwait(false);
        if (edge is null || !identity.CanAccessSite(edge.SiteId))
            return (ResourceNotFound("现场节点不存在。"), null);
        return (null, identity);
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
                "平台无法通过节点诊断凭据访问该现场节点，请检查节点凭据配置。",
                [("edgeStatus", (int)response.StatusCode)]);
        }

        return ProblemResponse((int)response.StatusCode, body, []);
    }
}
