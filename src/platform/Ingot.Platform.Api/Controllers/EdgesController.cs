using Ingot.Platform.Infrastructure.Services;
using Ingot.Contracts.Edge;
using Ingot.Platform.Api.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/edges")]
public class EdgesController(EdgeRegistry registry, EdgeTokenValidator edgeTokenValidator) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var edges = await registry.ListAsync(ct).ConfigureAwait(false);
        return Ok(edges.OrderByDescending(static edge => edge.LastSeen));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] EdgeRegistrationRequest request, CancellationToken ct)
    {
        if (!edgeTokenValidator.IsAuthorized(request.EdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");
        var now = DateTimeOffset.UtcNow;
        var state = await registry.UpsertAsync(
            request.EdgeId, request.HostBaseUrl, request.Hostname, null, now, ct).ConfigureAwait(false);
        return Ok(new { state.EdgeId, state.HostBaseUrl, state.Hostname, state.LastSeen });
    }

    [HttpPost("heartbeat")]
    [AllowAnonymous]
    public async Task<IActionResult> Heartbeat([FromBody] EdgeHeartbeatRequest request, CancellationToken ct)
    {
        if (!edgeTokenValidator.IsAuthorized(request.EdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");
        // 在线状态与历史排序以中心接收时间为准，避免现场时钟漂移把节点永久显示在未来或过去。
        var now = DateTimeOffset.UtcNow;
        var state = await registry.HeartbeatAsync(
            request.EdgeId,
            request.HostBaseUrl,
            request.LastError,
            request.Acquisition,
            now,
            request.Delivery,
            ct).ConfigureAwait(false);
        return Ok(new
        {
            state.EdgeId,
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
        => Ok(new { data = await registry.ListStatusHistoryAsync(edgeId, limit, ct).ConfigureAwait(false) });

    [HttpGet("{edgeId}/status-intervals")]
    public async Task<IActionResult> StatusIntervals(
        string edgeId, [FromQuery] int limit = 24, CancellationToken ct = default)
        => Ok(new { data = await registry.ListStatusIntervalsAsync(edgeId, limit, ct).ConfigureAwait(false) });
}
