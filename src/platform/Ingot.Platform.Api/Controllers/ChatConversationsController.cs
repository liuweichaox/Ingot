// 提供以会话和消息为中心的 Chat API；AgentRun 仅作为助手消息的执行明细。
using Ingot.Contracts.Agents;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Chat;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/chat/conversations")]
public sealed class ChatConversationsController(
    ChatConversationApplication chat,
    ProcessResearchQueries research,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 30,
        [FromQuery] DateTimeOffset? before = null,
        CancellationToken ct = default)
    {
        if (!TryActor(out var userId, out _, out var unauthorized))
            return unauthorized!;
        return Ok(await chat.ListAsync(userId!, before, limit, ct).ConfigureAwait(false));
    }

    [HttpGet("{conversationId}")]
    public async Task<IActionResult> Get(
        string conversationId,
        [FromQuery] long? beforeSequence = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        if (!TryActor(out var userId, out _, out var unauthorized))
            return unauthorized!;
        try
        {
            var value = await chat.GetAsync(conversationId, userId!, beforeSequence, limit, ct)
                .ConfigureAwait(false);
            return value is null ? ResourceNotFound() : Ok(value);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Start(
        [FromBody] StartChatConversationRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return InvalidRequest("请求体不能为空。");
        if (!TryActor(out var userId, out var identity, out var unauthorized))
            return unauthorized!;
        if (!await CanUsePageContextAsync(request.PageContext, identity!, ct).ConfigureAwait(false))
            return AuthorizationDenied();
        try
        {
            var accepted = await chat.StartAsync(
                userId!,
                request,
                AccessScope(identity!),
                ct).ConfigureAwait(false);
            return Accepted(accepted);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ServiceUnavailable(exception.Message);
        }
    }

    [HttpPost("{conversationId}/messages")]
    public async Task<IActionResult> Send(
        string conversationId,
        [FromBody] SendChatMessageRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return InvalidRequest("请求体不能为空。");
        if (!TryActor(out var userId, out var identity, out var unauthorized))
            return unauthorized!;
        try
        {
            var conversation = await chat.GetSummaryAsync(conversationId, userId!, ct).ConfigureAwait(false);
            if (conversation is null)
                return ResourceNotFound();
            if (!await CanUsePageContextAsync(conversation.PageContext, identity!, ct).ConfigureAwait(false))
                return AuthorizationDenied();
            var accepted = await chat.SendAsync(
                conversationId,
                userId!,
                request,
                AccessScope(identity!),
                ct).ConfigureAwait(false);
            return Accepted(accepted);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        catch (KeyNotFoundException)
        {
            return ResourceNotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return AuthorizationDenied();
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("仍在", StringComparison.Ordinal) ||
            exception.Message.Contains("归档", StringComparison.Ordinal))
        {
            return StateConflict(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ServiceUnavailable(exception.Message);
        }
    }

    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> Delete(string conversationId, CancellationToken ct)
    {
        if (!TryActor(out var userId, out _, out var unauthorized))
            return unauthorized!;
        try
        {
            return await chat.DeleteAsync(conversationId, userId!, ct).ConfigureAwait(false)
                ? NoContent()
                : ResourceNotFound();
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    private bool TryActor(
        out string? userId,
        out PlatformIdentity? identity,
        out IActionResult? error)
    {
        userId = userResolver.Resolve(User);
        identity = userResolver.ResolveIdentity(User);
        if (userId is null || identity is null)
        {
            error = AuthenticationRequired("需要先登录 Ingot 平台。");
            return false;
        }
        error = null;
        return true;
    }

    private async Task<bool> CanUsePageContextAsync(
        PageContextRef? pageContext,
        PlatformIdentity identity,
        CancellationToken ct)
    {
        if (pageContext is null)
            return true;
        if (!string.Equals(pageContext.Kind, "research-project", StringComparison.Ordinal))
            return false;
        if (!Guid.TryParse(pageContext.Id, out var projectId))
            return false;
        var project = await research.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        return project is not null &&
               (identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ||
                ((string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
                  project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal)) &&
                 identity.CanAccessSite(project.SiteCode)));
    }

    private static AgentRunAccessScopeSnapshot AccessScope(PlatformIdentity identity)
        => new()
        {
            AllowAllSites = identity.HasAnyRole(PlatformRoles.PlatformAdministrator),
            SiteIds = identity.HasAnyRole(PlatformRoles.PlatformAdministrator)
                ? []
                : identity.SiteIds.Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };
}
