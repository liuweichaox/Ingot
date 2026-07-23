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
        var now = request.Timestamp == default ? DateTimeOffset.UtcNow : request.Timestamp.ToUniversalTime();
        var state = registry.Heartbeat(request.EdgeId, request.HostBaseUrl, request.LastError, now);
        return Ok(new { state.EdgeId, state.HostBaseUrl, state.LastSeen, state.LastError });
    }
}
