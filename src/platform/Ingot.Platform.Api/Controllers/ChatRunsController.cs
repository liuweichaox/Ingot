// 管理 Chat 运行生命周期，并在读取历史或流事件时重新校验捕获的站点范围。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/chat/runs")]
public sealed class ChatRunsController(
    IAgentRuntime runtime,
    ProcessResearchQueries researchStore,
    PlatformUserResolver userResolver) : PlatformApiController
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChatRunRequest? request, CancellationToken ct)
    {
        if (!AgentContractValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        if (!TryAuthorize(out var userId, out var unauthorized))
            return unauthorized!;
        var identity = userResolver.ResolveIdentity(User)!;
        if (normalized!.PageContext is { Kind: "research-project" } pageContext)
        {
            if (!Guid.TryParse(pageContext.Id, out var projectId))
                return InvalidRequest("研发项目上下文标识无效。");
            var project = await researchStore.GetProjectAsync(projectId, ct).ConfigureAwait(false);
            var isAdministrator = identity.HasAnyRole(PlatformRoles.PlatformAdministrator);
            var canAccess = project is not null &&
                            (isAdministrator ||
                             ((string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
                               project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal)) &&
                              identity.CanAccessSite(project.SiteCode)));
            if (!canAccess)
                return AuthorizationDenied();
        }

        try
        {
            var run = await runtime.StartAsync(
                ProductEntryPoints.Chat,
                userId!,
                normalized!,
                new AgentAccessScope
                {
                    AllowAllSites = identity.HasAnyRole(PlatformRoles.PlatformAdministrator),
                    SiteIds = identity.SiteIds
                },
                ct).ConfigureAwait(false);
            return Accepted(new
            {
                runId = run.RunId,
                conversationId = run.ConversationId ?? run.RunId,
                status = run.Status,
                streamUrl = $"/api/v1/chat/runs/{run.RunId}/stream"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return AuthorizationDenied();
        }
        catch (InvalidOperationException ex)
        {
            return ServiceUnavailable(ex.Message);
        }
    }



    [HttpGet("{runId}")]
    public async Task<IActionResult> Get(string runId, CancellationToken ct)
    {
        if (!TryAuthorize(out var userId, out var unauthorized))
            return unauthorized!;
        var run = await runtime.GetAsync(ProductEntryPoints.Chat, runId, ct).ConfigureAwait(false);
        if (run is null)
            return ResourceNotFound();
        var identity = userResolver.ResolveIdentity(User)!;
        return CanAccessRun(run.UserId, run.AccessScope, identity)
            ? Ok(ToChatSnapshot(run))
            : ProblemResponse(StatusCodes.Status403Forbidden, "无权访问该 Chat 运行。", []);
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
        var identity = userResolver.ResolveIdentity(User)!;
        if (!CanAccessRun(run.UserId, run.AccessScope, identity))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
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
            return ResourceNotFound();
        var identity = userResolver.ResolveIdentity(User)!;
        if (!CanAccessRun(run.UserId, run.AccessScope, identity))
            return ProblemResponse(StatusCodes.Status403Forbidden, "无权访问该 Chat 运行。", []);

        var cancelled = await runtime.CancelAsync(
            ProductEntryPoints.Chat,
            runId,
            run.UserId,
            "用户请求取消 Chat 分析。",
            ct).ConfigureAwait(false);
        return cancelled
            ? Accepted(new { runId, status = "cancelling" })
            : StateConflict("Chat 运行已结束，无法取消。");
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
            IsDeterministic = capabilities.IsDeterministic,
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
        ConversationId = run.ConversationId ?? run.RunId,
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
        userId = userResolver.Resolve(User);
        if (userId is null)
        {
            error = AuthenticationRequired("需要先登录 Ingot 平台。");
            return false;
        }

        error = null;
        return true;
    }

    private static bool CanAccessRun(
        string ownerUserId,
        AgentRunAccessScopeSnapshot? capturedScope,
        PlatformIdentity identity)
    {
        if (!string.Equals(ownerUserId, identity.UserId, StringComparison.OrdinalIgnoreCase) ||
            capturedScope is null ||
            capturedScope.Version != AgentRunAccessScopeSnapshot.CurrentVersion)
            return false;
        if (identity.HasAnyRole(PlatformRoles.PlatformAdministrator))
            return true;
        if (capturedScope.AllowAllSites || capturedScope.SiteIds.Count == 0)
            return false;
        return capturedScope.SiteIds.All(identity.CanAccessSite);
    }
}
