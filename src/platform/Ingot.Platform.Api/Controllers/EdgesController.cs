using Ingot.Platform.Infrastructure.Services;
using Ingot.Contracts.Edge;
using Ingot.Platform.Api.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/edges")]
public class EdgesController(EdgeRegistry registry, EdgeTokenValidator edgeTokenValidator) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        return Ok(registry.List().OrderByDescending(e => e.LastSeen));
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public IActionResult Register([FromBody] EdgeRegistrationRequest request)
    {
        if (!edgeTokenValidator.IsAuthorized(request.EdgeId, Request.Headers.Authorization.ToString()))
            return Unauthorized(new { error = "边缘节点认证失败。" });
        var now = DateTimeOffset.UtcNow;
        var state = registry.Upsert(request.EdgeId, request.HostBaseUrl, request.Hostname, null, now);
        return Ok(new { state.EdgeId, state.HostBaseUrl, state.Hostname, state.LastSeen });
    }

    [HttpPost("heartbeat")]
    [AllowAnonymous]
    public IActionResult Heartbeat([FromBody] EdgeHeartbeatRequest request)
    {
        if (!edgeTokenValidator.IsAuthorized(request.EdgeId, Request.Headers.Authorization.ToString()))
            return Unauthorized(new { error = "边缘节点认证失败。" });
        // 在线状态与历史排序以中心接收时间为准，避免现场时钟漂移把节点永久显示在未来或过去。
        var now = DateTimeOffset.UtcNow;
        var state = registry.Heartbeat(
            request.EdgeId,
            request.HostBaseUrl,
            request.LastError,
            request.Acquisition,
            now,
            request.Delivery);
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
    public IActionResult StatusHistory(string edgeId, [FromQuery] int limit = 288)
        => Ok(new { data = registry.ListStatusHistory(edgeId, limit) });
}
