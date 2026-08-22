// 管理边缘节点注册、心跳和按站点隔离的运行状态查询。
using Ingot.Contracts.Edge;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/edges")]
public class EdgesController(
    EdgeRegistry registry,
    EdgeTokenValidator edgeTokenValidator,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(
        CancellationToken ct,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, true, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        var edges = await registry.ListAsync(ct, authorizedSiteId).ConfigureAwait(false);
        return Ok(edges.OrderByDescending(static edge => edge.LastSeen));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] EdgeRegistrationRequest request, CancellationToken ct)
    {
        if (!edgeTokenValidator.IsAuthorized(
                request.SiteId, request.EdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");
        if (string.IsNullOrWhiteSpace(request.SiteId))
            return InvalidRequest("边缘节点注册必须提供 SiteId。");
        var now = DateTimeOffset.UtcNow;
        EdgeRegistry.EdgeState state;
        try
        {
            state = await registry.UpsertAsync(
                request.EdgeId, request.SiteId, request.HostBaseUrl, request.Hostname, request.Version, now, ct)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return StateConflict(exception.Message);
        }
        return Ok(new { state.EdgeId, state.SiteId, state.HostBaseUrl, state.Hostname, state.LastSeen });
    }

    [HttpPost("heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> Heartbeat([FromBody] EdgeHeartbeatRequest request, CancellationToken ct)
    {
        if (!edgeTokenValidator.IsAuthorized(
                request.SiteId, request.EdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");
        if (string.IsNullOrWhiteSpace(request.SiteId))
            return InvalidRequest("边缘节点心跳必须提供 SiteId。");

        var now = DateTimeOffset.UtcNow;
        EdgeRegistry.EdgeState state;
        try
        {
            state = await registry.HeartbeatAsync(
                request.EdgeId,
                request.SiteId,
                request.HostBaseUrl,
                request.LastError,
                request.Acquisition,
                now,
                request.Delivery,
                ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return StateConflict(exception.Message);
        }
        return Ok(new
        {
            state.EdgeId,
            state.SiteId,
            state.HostBaseUrl,
            state.LastSeen,
            state.LastError,
            state.Acquisition,
            state.Delivery
        });
    }

    [HttpGet("{edgeId}/status-history")]
    public async Task<IActionResult> StatusHistory(
        string edgeId, [FromQuery] int limit = 288, CancellationToken ct = default)
    {
        var denied = await DeniedEdgeAsync(edgeId, ct).ConfigureAwait(false);
        return denied ?? Ok(new { data = await registry.ListStatusHistoryAsync(edgeId, limit, ct).ConfigureAwait(false) });
    }

    [HttpGet("{edgeId}/status-intervals")]
    public async Task<IActionResult> StatusIntervals(
        string edgeId, [FromQuery] int limit = 24, CancellationToken ct = default)
    {
        var denied = await DeniedEdgeAsync(edgeId, ct).ConfigureAwait(false);
        return denied ?? Ok(new { data = await registry.ListStatusIntervalsAsync(edgeId, limit, ct).ConfigureAwait(false) });
    }

    private async Task<IActionResult?> DeniedEdgeAsync(string edgeId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var edge = await registry.FindAsync(edgeId, ct).ConfigureAwait(false);
        if (edge is null || !identity.CanAccessSite(edge.SiteId))
            return ResourceNotFound("采集节点不存在。");
        return null;
    }
}
