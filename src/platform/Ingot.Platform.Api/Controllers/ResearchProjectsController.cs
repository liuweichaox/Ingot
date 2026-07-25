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
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListProjectsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{projectId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await workflow.GetWorkspaceAsync(projectId, ct).ConfigureAwait(false));
        }
        catch (ProcessResearchRuleException exception)
        {
            return NotFound(new { error = exception.Message });
        }
    }

    [HttpPost]
    public Task<IActionResult> Create(
        [FromBody] ResearchProject request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.CreateProjectAsync(request, ResolveUserId()!, ct));

    [HttpPut("{projectId:guid}")]
    public Task<IActionResult> Update(
        Guid projectId,
        [FromBody] ResearchProject request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.UpdateProjectAsync(projectId, request, ResolveUserId()!, ct));

    [HttpPost("{projectId:guid}/status")]
    public Task<IActionResult> ChangeStatus(
        Guid projectId,
        [FromBody] ResearchStatusChangeRequest request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.ChangeProjectStatusAsync(
                projectId,
                request.TargetStatus,
                ResolveUserId()!,
                ct));

    [HttpPost("{projectId:guid}/hypotheses")]
    public Task<IActionResult> SaveHypothesis(
        Guid projectId,
        [FromBody] ResearchHypothesis request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.SaveHypothesisAsync(
                projectId,
                request,
                ResolveUserId()!,
                ct));

    [HttpPost("{projectId:guid}/experiments")]
    public Task<IActionResult> CreateExperiment(
        Guid projectId,
        [FromBody] ResearchExperiment request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.CreateExperimentAsync(
                projectId,
                request,
                ResolveUserId()!,
                ct));

    [HttpPost("experiments/{experimentId:guid}/status")]
    public Task<IActionResult> ChangeExperimentStatus(
        Guid experimentId,
        [FromBody] ResearchStatusChangeRequest request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.ChangeExperimentStatusAsync(
                experimentId,
                request.TargetStatus,
                ResolveUserId()!,
                ct));

    [HttpPost("{projectId:guid}/process-windows")]
    public Task<IActionResult> SaveProcessWindow(
        Guid projectId,
        [FromBody] ResearchProcessWindow request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.SaveProcessWindowAsync(
                projectId,
                request,
                ResolveUserId()!,
                ct));

    [HttpPost("process-windows/{windowId:guid}/validate")]
    public Task<IActionResult> ValidateProcessWindow(
        Guid windowId,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.ValidateProcessWindowAsync(
                windowId,
                ResolveUserId()!,
                ct));

    [HttpPost("{projectId:guid}/knowledge-claims")]
    public Task<IActionResult> SaveKnowledgeClaim(
        Guid projectId,
        [FromBody] ResearchKnowledgeClaim request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.SaveKnowledgeClaimAsync(
                projectId,
                request,
                ResolveUserId()!,
                ct));

    [HttpPost("knowledge-claims/{claimId:guid}/review")]
    public Task<IActionResult> ReviewKnowledgeClaim(
        Guid claimId,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.ReviewKnowledgeClaimAsync(
                claimId,
                ResolveUserId()!,
                ct));

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ProcessResearchRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

public sealed record ResearchStatusChangeRequest(string TargetStatus);
