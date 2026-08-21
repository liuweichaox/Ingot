
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/research-projects/{projectId:guid}/mechanism-claims")]
public sealed class MechanismKnowledgeController(
    MechanismKnowledgeQueries store,
    MechanismKnowledgeService service,
    MechanismClaimDraftService draftService,
    ProcessResearchQueries researchStore,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, false, ct).ConfigureAwait(false);
        return access.Result ?? Ok(new { data = await store.ListClaimsAsync(projectId, ct).ConfigureAwait(false) });
    }

    [HttpGet("{claimId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, Guid claimId, [FromQuery] int? version, CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, false, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        var value = await store.GetClaimAsync(claimId, version, ct).ConfigureAwait(false);
        return value is null || value.ProjectId != projectId ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> SaveDraft(
        Guid projectId,
        [FromBody] MechanismClaimVersion request,
        CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        return await ExecuteAsync(() => service.SaveDraftAsync(
            projectId, request, access.Identity!.UserId, ct)).ConfigureAwait(false);
    }

    [HttpPost("draft-from-source")]
    public async Task<IActionResult> GenerateDraft(
        Guid projectId,
        [FromBody] MechanismClaimDraftGenerationRequest request,
        CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        return await ExecuteAsync(() => draftService.GenerateAsync(
            projectId, request, access.Identity!.UserId, ct)).ConfigureAwait(false);
    }

    [HttpPost("{claimId:guid}/review")]
    public async Task<IActionResult> Review(
        Guid projectId,
        Guid claimId,
        [FromBody] MechanismClaimReviewRequest request,
        CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        var claim = await store.GetClaimAsync(claimId, null, ct).ConfigureAwait(false);
        if (claim is null || claim.ProjectId != projectId) return ResourceNotFound();
        return await ExecuteAsync(() => service.ReviewAsync(
            claimId, request, access.Identity!.UserId, ct)).ConfigureAwait(false);
    }

    [HttpPost("{claimId:guid}/lifecycle")]
    public async Task<IActionResult> Transition(
        Guid projectId,
        Guid claimId,
        [FromBody] MechanismClaimLifecycleRequest request,
        CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        return await ExecuteAsync(() => service.TransitionAsync(
            projectId, claimId, request, access.Identity!.UserId, ct)).ConfigureAwait(false);
    }

    [HttpGet("conflicts")]
    public async Task<IActionResult> ListConflicts(Guid projectId, CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, false, ct).ConfigureAwait(false);
        return access.Result ?? Ok(new { data = await store.ListConflictsAsync(projectId, ct).ConfigureAwait(false) });
    }

    [HttpPost("conflicts")]
    public async Task<IActionResult> AddConflict(
        Guid projectId,
        [FromBody] MechanismClaimConflictRequest request,
        CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        return await ExecuteAsync(() => service.AddConflictAsync(
            projectId, request, access.Identity!.UserId, ct)).ConfigureAwait(false);
    }

    [HttpPost("conflicts/{conflictId:guid}/resolve")]
    public async Task<IActionResult> ResolveConflict(
        Guid projectId,
        Guid conflictId,
        [FromBody] MechanismClaimConflictResolutionRequest request,
        CancellationToken ct)
    {
        var access = await ResolveAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null) return access.Result;
        return await ExecuteAsync(() => service.ResolveConflictAsync(
            projectId, conflictId, request, access.Identity!.UserId, ct)).ConfigureAwait(false);
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action().ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    private async Task<(PlatformIdentity? Identity, IActionResult? Result)> ResolveAccessAsync(
        Guid projectId,
        bool requireWrite,
        CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null) return (null, AuthenticationRequired("需要平台统一认证。"));
        if (!identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator))
            return (null, AuthorizationDenied());
        var project = await researchStore.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null) return (null, ResourceNotFound("研发项目不存在。"));
        var canAccess = identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ||
                        string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
                        project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal);
        if (!canAccess || requireWrite && (project.Status is
                ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived))
            return (null, AuthorizationDenied());
        return (identity, null);
    }
}
