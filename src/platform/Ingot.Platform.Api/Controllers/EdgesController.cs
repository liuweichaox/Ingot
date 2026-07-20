using Ingot.Platform.Infrastructure.Services;
using Ingot.Contracts.Edge;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/edges")]
public class EdgesController(EdgeRegistry registry) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        return Ok(registry.List().OrderByDescending(e => e.LastSeen));
    }

    [HttpPost("register")]
    public IActionResult Register([FromBody] EdgeRegistrationRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var state = registry.Upsert(request.EdgeId, request.HostBaseUrl, request.Hostname, null, now);
        return Ok(new { state.EdgeId, state.HostBaseUrl, state.Hostname, state.LastSeen });
    }

    [HttpPost("heartbeat")]
    public IActionResult Heartbeat([FromBody] EdgeHeartbeatRequest request)
    {
        var now = request.Timestamp == default ? DateTimeOffset.UtcNow : request.Timestamp.ToUniversalTime();
        var state = registry.Heartbeat(request.EdgeId, request.HostBaseUrl, request.LastError, now);
        return Ok(new { state.EdgeId, state.HostBaseUrl, state.LastSeen, state.LastError });
    }
}
