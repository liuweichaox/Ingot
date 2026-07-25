using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/research-projects")]
public sealed class ResearchProjectsController(
    IProcessResearchStore store,
    ProcessResearchWorkflow workflow,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null)
            return identity.Result;
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(offset, 0);
        return Ok(new
        {
            data = await store.ListProjectsAsync(
                identity.Identity!.UserId,
                identity.Identity.HasAnyRole(PlatformRoles.PlatformAdministrator),
                limit,
                offset,
                ct).ConfigureAwait(false),
            limit,
            offset
        });
    }

    [HttpGet("{projectId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
        => await ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await workflow.GetWorkspaceAsync(projectId, ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] ResearchProject request,
        CancellationToken ct)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null)
            return identity.Result;
        return await ExecuteRuleAsync(
            async () => Ok(await workflow.CreateProjectAsync(
                request,
                identity.Identity!.UserId,
                ct).ConfigureAwait(false))).ConfigureAwait(false);
    }

    [HttpPut("{projectId:guid}")]
    public Task<IActionResult> Update(
        Guid projectId,
        [FromBody] ResearchProject request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.UpdateProjectAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/status")]
    public Task<IActionResult> ChangeStatus(
        Guid projectId,
        [FromBody] ResearchStatusChangeRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.ChangeProjectStatusAsync(
                projectId,
                request.TargetStatus,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/hypotheses")]
    public Task<IActionResult> SaveHypothesis(
        Guid projectId,
        [FromBody] ResearchHypothesis request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.SaveHypothesisAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/experiments")]
    public Task<IActionResult> CreateExperiment(
        Guid projectId,
        [FromBody] ResearchExperiment request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.CreateExperimentAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("experiments/{experimentId:guid}/status")]
    public async Task<IActionResult> ChangeExperimentStatus(
        Guid experimentId,
        [FromBody] ResearchStatusChangeRequest request,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return NotFound(new { error = "实验不存在。" });
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await workflow.ChangeExperimentStatusAsync(
                experimentId,
                request.TargetStatus,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("experiments/{experimentId:guid}/results")]
    public async Task<IActionResult> RecordExperimentResult(
        Guid experimentId,
        [FromBody] ResearchExperimentResult request,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return NotFound(new { error = "实验不存在。" });
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await workflow.RecordExperimentResultAsync(
                experimentId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("{projectId:guid}/process-windows")]
    public Task<IActionResult> SaveProcessWindow(
        Guid projectId,
        [FromBody] ResearchProcessWindow request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.SaveProcessWindowAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("process-windows/{windowId:guid}/validate")]
    public async Task<IActionResult> ValidateProcessWindow(Guid windowId, CancellationToken ct)
    {
        var window = await store.GetProcessWindowAsync(windowId, ct).ConfigureAwait(false);
        if (window is null)
            return NotFound(new { error = "工艺窗口不存在。" });
        return await ExecuteForProjectAsync(
            window.ProjectId,
            true,
            async identity => Ok(await workflow.ValidateProcessWindowAsync(
                windowId,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("{projectId:guid}/knowledge-claims")]
    public Task<IActionResult> SaveKnowledgeClaim(
        Guid projectId,
        [FromBody] ResearchKnowledgeClaim request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.SaveKnowledgeClaimAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("knowledge-claims/{claimId:guid}/review")]
    public async Task<IActionResult> ReviewKnowledgeClaim(Guid claimId, CancellationToken ct)
    {
        var claim = await store.GetKnowledgeClaimAsync(claimId, ct).ConfigureAwait(false);
        if (claim is null)
            return NotFound(new { error = "知识声明不存在。" });
        return await ExecuteForProjectAsync(
            claim.ProjectId,
            true,
            async identity => Ok(await workflow.ReviewKnowledgeClaimAsync(
                claimId,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    private async Task<IActionResult> ExecuteForProjectAsync(
        Guid projectId,
        bool requireWrite,
        Func<PlatformIdentity, Task<IActionResult>> operation,
        CancellationToken ct)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null)
            return identity.Result;
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
            return NotFound(new { error = "研发项目不存在。" });
        if (!CanAccess(project, identity.Identity!, requireWrite))
            return Forbid();
        return await ExecuteRuleAsync(() => operation(identity.Identity!)).ConfigureAwait(false);
    }

    private (PlatformIdentity? Identity, IActionResult? Result) ResolveResearchIdentity()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return (null, Unauthorized(new { error = "需要平台统一认证。" }));
        if (!identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator))
            return (null, Forbid());
        return (identity, null);
    }

    private static bool CanAccess(
        ResearchProject project,
        PlatformIdentity identity,
        bool requireWrite)
    {
        if (identity.HasAnyRole(PlatformRoles.PlatformAdministrator))
            return true;
        var isMember = string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
                       project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal);
        return isMember && (!requireWrite || project.Status != ResearchProjectStatuses.Archived);
    }

    private static async Task<IActionResult> ExecuteRuleAsync(Func<Task<IActionResult>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (ProcessResearchRuleException exception)
        {
            return new ConflictObjectResult(new { error = exception.Message });
        }
    }
}

public sealed record ResearchStatusChangeRequest(string TargetStatus);
