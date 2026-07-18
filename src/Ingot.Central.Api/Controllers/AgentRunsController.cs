using System.Text.Json;
using Ingot.Agent;
using Ingot.Central.Api.Agents;
using Ingot.Contracts.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/agent/runs")]
public sealed class AgentRunsController(
    IAgentRuntime runtime,
    AgentTokenValidator tokenValidator) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAgentRunRequest? request,
        CancellationToken ct)
    {
        if (!AgentContractValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        if (!TryAuthorize(out var actorId, out var unauthorized))
            return unauthorized!;

        try
        {
            var run = await runtime.StartAsync(ProductSurfaces.Agent, actorId!, normalized!, ct).ConfigureAwait(false);
            return Accepted(new
            {
                runId = run.RunId,
                status = run.Status,
                streamUrl = $"/api/v1/agent/runs/{run.RunId}/stream"
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpGet("{runId}")]
    public async Task<IActionResult> Get(string runId, CancellationToken ct)
    {
        if (!IsDesktopRequest())
            return DesktopOnly();
        var run = await runtime.GetAsync(ProductSurfaces.Agent, runId, ct).ConfigureAwait(false);
        if (run is null)
            return NotFound();
        return IsAuthorized(run.ActorId)
            ? Ok(run)
            : Unauthorized(new { error = "Ingot Agent Desktop 运行访问凭据无效。" });
    }

    [HttpGet("{runId}/stream")]
    public async Task Stream(string runId, CancellationToken ct)
    {
        if (!IsDesktopRequest())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        var run = await runtime.GetAsync(ProductSurfaces.Agent, runId, ct).ConfigureAwait(false);
        if (run is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!IsAuthorized(run.ActorId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var afterSequence = 0L;
        if (long.TryParse(Request.Headers["Last-Event-ID"].FirstOrDefault(), out var parsed))
            afterSequence = Math.Max(0, parsed);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var item in runtime.StreamAsync(ProductSurfaces.Agent, runId, afterSequence, ct).ConfigureAwait(false))
        {
            await Response.WriteAsync($"id: {item.Sequence}\n", ct).ConfigureAwait(false);
            await Response.WriteAsync($"event: {item.Type}\n", ct).ConfigureAwait(false);
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(item, JsonOptions)}\n\n", ct)
                .ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    [HttpPost("{runId}:cancel")]
    public async Task<IActionResult> Cancel(string runId, CancellationToken ct)
    {
        if (!IsDesktopRequest())
            return DesktopOnly();
        var run = await runtime.GetAsync(ProductSurfaces.Agent, runId, ct).ConfigureAwait(false);
        if (run is null)
            return NotFound();
        if (!IsAuthorized(run.ActorId))
            return Unauthorized(new { error = "Ingot Agent Desktop 运行访问凭据无效。" });

        var cancelled = await runtime.CancelAsync(ProductSurfaces.Agent, runId, run.ActorId, "用户请求取消代码生成。", ct)
            .ConfigureAwait(false);
        return cancelled ? Accepted(new { runId, status = "cancelling" }) : Conflict(new
        {
            error = "运行已结束，无法取消。"
        });
    }

    [HttpGet("/api/v1/agent/capabilities")]
    public IActionResult Capabilities()
    {
        if (!TryAuthorize(out _, out var unauthorized))
            return unauthorized!;
        var capabilities = runtime.GetCapabilities(ProductSurfaces.Agent);
        return Ok(new DesktopAgentCapabilities
        {
            Enabled = capabilities.Enabled,
            Provider = capabilities.Provider,
            FastModel = capabilities.FastModel,
            ReasoningModel = capabilities.ReasoningModel,
            Modes = capabilities.Modes,
            Tools = capabilities.Tools,
            ArtifactKinds = capabilities.ArtifactKinds,
            ConnectorSpecificationWorkflow = capabilities.ConnectorSpecificationWorkflow,
            ConnectorWorkspaceWorkflow = capabilities.ConnectorWorkspaceWorkflow,
            MaxIterations = capabilities.MaxIterations,
            MaxToolCalls = capabilities.MaxToolCalls,
            MaxRunSeconds = capabilities.MaxRunSeconds
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 20,
        [FromQuery] DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        if (!TryAuthorize(out var actorId, out var unauthorized))
            return unauthorized!;
        return Ok(await runtime.ListAsync(ProductSurfaces.Agent, actorId!, before, limit, ct).ConfigureAwait(false));
    }

    private bool TryAuthorize(out string? actorId, out IActionResult? error)
    {
        if (!IsDesktopRequest())
        {
            actorId = null;
            error = DesktopOnly();
            return false;
        }
        actorId = Request.Headers["X-Ingot-Actor"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            error = BadRequest(new { error = "必须提供 X-Ingot-Actor。" });
            return false;
        }
        if (!IsAuthorized(actorId))
        {
            error = Unauthorized(new { error = "Ingot Agent Desktop 访问凭据无效。" });
            return false;
        }

        actorId = tokenValidator.CanonicalizeActorId(actorId);
        error = null;
        return true;
    }

    private bool IsAuthorized(string actorId)
        => IsDesktopRequest() &&
           tokenValidator.IsAuthorized(actorId, Request.Headers.Authorization.FirstOrDefault());

    private bool IsDesktopRequest()
        => AgentTokenValidator.IsDesktopClient(Request.Headers["X-Ingot-Client"].FirstOrDefault());

    private ObjectResult DesktopOnly()
        => StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = $"此接口仅供 Ingot Agent Desktop 使用，必须提供 X-Ingot-Client: {AgentTokenValidator.DesktopClientId}。"
        });
}
