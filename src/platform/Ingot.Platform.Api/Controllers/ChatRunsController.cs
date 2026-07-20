using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Api.Agents;
using Ingot.Contracts.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/chat/runs")]
public sealed class ChatRunsController(
    IAgentRuntime runtime,
    ChatTokenValidator tokenValidator) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChatRunRequest? request, CancellationToken ct)
    {
        if (!AgentContractValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        if (!TryAuthorize(out var userId, out var unauthorized))
            return unauthorized!;

        try
        {
            var run = await runtime.StartAsync(
                ProductEntryPoints.Chat,
                userId!,
                normalized!,
                ct).ConfigureAwait(false);
            return Accepted(new
            {
                runId = run.RunId,
                status = run.Status,
                streamUrl = $"/api/v1/chat/runs/{run.RunId}/stream"
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 20,
        [FromQuery] DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        if (!TryAuthorize(out var userId, out var unauthorized))
            return unauthorized!;
        var page = await runtime.ListAsync(ProductEntryPoints.Chat, userId!, before, limit, ct).ConfigureAwait(false);
        return Ok(new ChatRunPage
        {
            Items = page.Items.Select(static run => new ChatRunListItem
            {
                RunId = run.RunId,
                Question = run.Question,
                EntryPoint = ProductEntryPoints.Chat,
                Purpose = RunPurposes.ReadOnlyAnalysis,
                Mode = run.Mode,
                Status = run.Status,
                CreatedAt = run.CreatedAt,
                CompletedAt = run.CompletedAt,
                Summary = run.Summary,
                Usage = run.Usage
            }).ToArray(),
            NextBefore = page.NextBefore
        });
    }

    [HttpGet("{runId}")]
    public async Task<IActionResult> Get(string runId, CancellationToken ct)
    {
        if (!TryAuthorize(out var userId, out var unauthorized))
            return unauthorized!;
        var run = await runtime.GetAsync(ProductEntryPoints.Chat, runId, ct).ConfigureAwait(false);
        if (run is null)
            return NotFound();
        return string.Equals(run.UserId, userId, StringComparison.OrdinalIgnoreCase)
            ? Ok(ToChatSnapshot(run))
            : Unauthorized(new { error = "Chat 运行访问凭据无效。" });
    }

    [HttpGet("{runId}/stream")]
    public async Task Stream(string runId, CancellationToken ct)
    {
        if (!TryAuthorize(out var userId, out var unauthorized))
        {
            Response.StatusCode = (unauthorized as ObjectResult)?.StatusCode ?? StatusCodes.Status401Unauthorized;
            return;
        }
        var run = await runtime.GetAsync(ProductEntryPoints.Chat, runId, ct).ConfigureAwait(false);
        if (run is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!string.Equals(run.UserId, userId, StringComparison.OrdinalIgnoreCase))
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

        await foreach (var item in runtime.StreamAsync(ProductEntryPoints.Chat, runId, afterSequence, ct)
                           .ConfigureAwait(false))
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
        if (!TryAuthorize(out var userId, out var unauthorized))
            return unauthorized!;
        var run = await runtime.GetAsync(ProductEntryPoints.Chat, runId, ct).ConfigureAwait(false);
        if (run is null)
            return NotFound();
        if (!string.Equals(run.UserId, userId, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "Chat 运行访问凭据无效。" });

        var cancelled = await runtime.CancelAsync(
            ProductEntryPoints.Chat,
            runId,
            run.UserId,
            "用户请求取消 Chat 分析。",
            ct).ConfigureAwait(false);
        return cancelled ? Accepted(new { runId, status = "cancelling" }) : Conflict(new
        {
            error = "Chat 运行已结束，无法取消。"
        });
    }

    [HttpGet("/api/v1/chat/capabilities")]
    public IActionResult Capabilities()
    {
        if (!TryAuthorize(out _, out var unauthorized))
            return unauthorized!;
        var capabilities = runtime.GetCapabilities(ProductEntryPoints.Chat);
        return Ok(new ChatCapabilities
        {
            EntryPoint = capabilities.EntryPoint,
            Purpose = capabilities.Purpose,
            Enabled = capabilities.Enabled,
            CombinedAnalysisEnabled = capabilities.CombinedAnalysisEnabled,
            Provider = capabilities.Provider,
            FastModel = capabilities.FastModel,
            ReasoningModel = capabilities.ReasoningModel,
            Modes = capabilities.Modes,
            Roles = capabilities.Roles,
            Tools = capabilities.Tools.Select(static tool => new ChatToolCapability
            {
                Name = tool.Name,
                Version = tool.Version,
                Description = tool.Description,
                Access = tool.Access
            }).ToArray(),
            MaxToolCalls = capabilities.MaxToolCalls,
            MaxRunSeconds = capabilities.MaxRunSeconds,
            MaxDiscussionRounds = capabilities.MaxDiscussionRounds,
            MaxDiscussionTurns = capabilities.MaxDiscussionTurns
        });
    }

    private static ChatRunSnapshot ToChatSnapshot(AgentRunSnapshot run) => new()
    {
        RunId = run.RunId,
        UserId = run.UserId,
        EntryPoint = run.EntryPoint,
        Purpose = run.Purpose,
        Question = run.Question,
        PageContext = run.PageContext,
        Mode = run.Mode,
        Status = run.Status,
        ModelProvider = run.ModelProvider,
        Model = run.Model,
        PromptVersion = run.PromptVersion,
        ToolsetVersion = run.ToolsetVersion,
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Plan = run.Plan,
        WorkflowStage = run.WorkflowStage,
        Iteration = run.Iteration,
        ToolInvocations = run.ToolInvocations.Select(static tool => new ChatToolInvocation
        {
            Tool = tool.Tool,
            Version = tool.Version,
            Status = tool.Status,
            StartedAt = tool.StartedAt,
            CompletedAt = tool.CompletedAt,
            Summary = tool.Summary,
            Error = tool.Error,
            RelatedRecords = tool.RelatedRecords
        }).ToArray(),
        Answer = run.Answer,
        Usage = run.Usage,
        Error = run.Error,
        CancellationReason = run.CancellationReason
    };

    private bool TryAuthorize(out string? userId, out IActionResult? error)
    {
        userId = Request.Headers["X-Ingot-User"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            error = BadRequest(new { error = "必须提供 X-Ingot-User。" });
            return false;
        }
        if (!IsAuthorized(userId))
        {
            error = Unauthorized(new { error = "Chat 访问凭据无效。" });
            return false;
        }

        userId = tokenValidator.CanonicalizeUserId(userId);
        error = null;
        return true;
    }

    private bool IsAuthorized(string userId)
        => tokenValidator.IsAuthorized(userId, Request.Headers.Authorization.FirstOrDefault());
}
