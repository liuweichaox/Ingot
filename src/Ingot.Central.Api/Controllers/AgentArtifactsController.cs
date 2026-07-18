using Ingot.Agent;
using Ingot.Central.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/agent/artifacts")]
public sealed class AgentArtifactsController(
    IAgentArtifactStore store,
    AgentTokenValidator tokenValidator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        var items = await store.ListAsync(actorId!, Math.Clamp(limit, 1, 100), ct).ConfigureAwait(false);
        return Ok(new { items });
    }

    [HttpGet("{artifactId}")]
    public async Task<IActionResult> Get(string artifactId, CancellationToken ct = default)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        var artifact = await store.GetAsync(actorId!, artifactId, ct).ConfigureAwait(false);
        return artifact is null ? NotFound() : Ok(artifact);
    }

    private bool TryAuthorize(out string? actorId, out IActionResult? error)
    {
        if (!AgentTokenValidator.IsDesktopClient(Request.Headers["X-Ingot-Client"].FirstOrDefault()))
        {
            actorId = null;
            error = StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = $"此接口仅供 Ingot Agent Desktop 使用，必须提供 X-Ingot-Client: {AgentTokenValidator.DesktopClientId}。"
            });
            return false;
        }
        actorId = Request.Headers["X-Ingot-Actor"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            error = BadRequest(new { error = "必须提供 X-Ingot-Actor。" });
            return false;
        }
        if (!tokenValidator.IsAuthorized(actorId, Request.Headers.Authorization.FirstOrDefault()))
        {
            error = Unauthorized(new { error = "Agent 制品访问凭据无效。" });
            return false;
        }
        actorId = tokenValidator.CanonicalizeActorId(actorId);
        error = null;
        return true;
    }
}
