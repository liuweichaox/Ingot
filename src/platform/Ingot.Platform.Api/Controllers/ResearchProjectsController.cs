// 暴露项目、真实生产观察和日常配方建议闭环 API。
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/research-projects")]
public sealed class ResearchProjectsController(
    ProcessResearchQueries store,
    ProcessResearchWorkflow workflow,
    ResearchOptimizationService optimizationService,
    ResearchRecipeRecommendationDecisionService recipeRecommendationDecisions,
    IResearchObservationAssembler observationAssembler,
    ResearchExecutionEvidenceService executionEvidence,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet("{projectId:guid}/recipe-recommendations")]
    public Task<IActionResult> ListRecipeRecommendations(Guid projectId, [FromQuery] string? cursor,
        [FromQuery] int limit = 100, CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListRecipeRecommendationsPageAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/recipe-recommendation-decisions")]
    public Task<IActionResult> ListRecipeRecommendationDecisions(Guid projectId, [FromQuery] string? cursor,
        [FromQuery] int limit = 100, CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListRecipeRecommendationDecisionsPageAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/recipe-recommendation-flows")]
    public Task<IActionResult> ListRecipeRecommendationFlows(Guid projectId, [FromQuery] string? cursor,
        [FromQuery] int limit = 100, CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => workflow.ListRecipeRecommendationFlowsAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/audit")]
    public Task<IActionResult> ListAudit(Guid projectId, [FromQuery] string? cursor,
        [FromQuery] int limit = 100, CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListAuditEntriesPageAsync(projectId, value, limit, ct), ct);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int limit = 50, [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null) return identity.Result;
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(offset, 0);
        return Ok(new { data = await store.ListProjectsAsync(identity.Identity!.UserId,
            identity.Identity.HasAnyRole(PlatformRoles.PlatformAdministrator), identity.Identity.SiteIds,
            limit, offset, ct).ConfigureAwait(false), limit, offset });
    }

    [HttpGet("{projectId:guid}")]
    public Task<IActionResult> Get(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(projectId, false,
            async _ => Ok(await workflow.GetWorkspaceAsync(projectId, ct).ConfigureAwait(false)), ct);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ResearchProject request, CancellationToken ct)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null) return identity.Result;
        var siteScope = PlatformSiteScope.Resolve(identity.Identity!, request.SiteCode, false, out var siteId);
        if (siteScope == SiteScopeFailure.Forbidden) return AuthorizationDenied();
        if (siteScope == SiteScopeFailure.Missing) return InvalidRequest("研发项目必须绑定一个有权访问的站点。");
        return await ExecuteRuleAsync(async () => Ok(await workflow.CreateProjectAsync(
            request with { SiteCode = siteId }, identity.Identity!.UserId, ct).ConfigureAwait(false))).ConfigureAwait(false);
    }

    [HttpPut("{projectId:guid}")]
    public Task<IActionResult> Update(Guid projectId, [FromBody] ResearchProject request, CancellationToken ct)
        => ExecuteForProjectAsync(projectId, true, async identity =>
        {
            var siteScope = PlatformSiteScope.Resolve(identity, request.SiteCode, false, out var siteId);
            if (siteScope == SiteScopeFailure.Forbidden) return AuthorizationDenied();
            if (siteScope == SiteScopeFailure.Missing) return InvalidRequest("研发项目必须绑定一个有权访问的站点。");
            return Ok(await workflow.UpdateProjectAsync(projectId, request with { SiteCode = siteId }, identity.UserId, ct)
                .ConfigureAwait(false));
        }, ct);

    [HttpPatch("{projectId:guid}/members")]
    public async Task<IActionResult> UpdateMembers(Guid projectId,
        [FromBody] ResearchProjectMembersUpdateRequest request, CancellationToken ct)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null) return identity.Result;
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null) return ResourceNotFound("研发项目不存在。");
        var isAdministrator = identity.Identity!.HasAnyRole(PlatformRoles.PlatformAdministrator);
        if (!isAdministrator && (!string.Equals(project.OwnerUserId, identity.Identity.UserId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(project.SiteCode) || !identity.Identity.CanAccessSite(project.SiteCode)))
            return AuthorizationDenied("只有项目负责人或平台管理员可以管理项目成员。");
        return await ExecuteRuleAsync(async () => Ok(await workflow.UpdateProjectMembersAsync(projectId,
            request.Revision, request.MemberUserIds, identity.Identity.UserId, isAdministrator, ct).ConfigureAwait(false)));
    }

    [HttpPost("{projectId:guid}/status")]
    public Task<IActionResult> ChangeStatus(Guid projectId, [FromBody] ResearchStatusChangeRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(projectId, false, async identity => Ok(await workflow.ChangeProjectStatusAsync(
            projectId, request.TargetStatus, identity.UserId, ct,
            identity.HasAnyRole(PlatformRoles.PlatformAdministrator), request.Revision).ConfigureAwait(false)), ct);

    [HttpPost("{projectId:guid}/hypotheses")]
    public Task<IActionResult> SaveHypothesis(Guid projectId, [FromBody] ResearchHypothesis request, CancellationToken ct)
        => ExecuteForProjectAsync(projectId, true, async identity => Ok(await workflow.SaveHypothesisAsync(
            projectId, request, identity.UserId, ct).ConfigureAwait(false)), ct);

    [HttpPost("{projectId:guid}/hypotheses/from-execution-comparison")]
    public Task<IActionResult> ProposeHypothesesFromExecutionComparison(Guid projectId,
        [FromBody] ResearchHypothesisFromExecutionComparisonRequest request, CancellationToken ct)
        => ExecuteForProjectAsync(projectId, true, async identity => Ok(await executionEvidence.ProposeHypothesesAsync(
            projectId, request, identity.UserId, ct).ConfigureAwait(false)), ct);

    [HttpPost("{projectId:guid}/recipe-recommendations")]
    public Task<IActionResult> CreateRecipeRecommendation(Guid projectId,
        [FromBody] ResearchRecipeRecommendationRequest request, CancellationToken ct)
        => ExecuteForProjectAsync(projectId, true, async identity => Ok(await optimizationService
            .CreateNextRecipeRecommendationAsync(projectId, request, identity.UserId, ct).ConfigureAwait(false)), ct);

    [HttpPost("recipe-recommendations/{recommendationId:guid}/items/{recommendationKey}/decision")]
    public async Task<IActionResult> RecordRecipeRecommendationDecision(Guid recommendationId, string recommendationKey,
        [FromBody] ResearchRecipeRecommendationDecisionRequest request, CancellationToken ct)
    {
        var recommendation = await store.GetRecipeRecommendationAsync(recommendationId, ct).ConfigureAwait(false);
        if (recommendation is null) return ResourceNotFound("下一配方建议不存在。");
        return await ExecuteForProjectAsync(recommendation.ProjectId, true, async identity => Ok(await recipeRecommendationDecisions
            .RecordDecisionAsync(recommendationId, recommendationKey, request, identity.UserId, ct).ConfigureAwait(false)), ct);
    }

    [HttpPost("recipe-recommendation-decisions/{decisionId:guid}/execution-link")]
    public async Task<IActionResult> LinkRecipeRecommendationExecution(Guid decisionId,
        [FromBody] ResearchRecipeRecommendationExecutionLinkRequest request, CancellationToken ct)
    {
        var decision = await store.GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false);
        if (decision is null) return ResourceNotFound("日常配方建议决策不存在。");
        return await ExecuteForProjectAsync(decision.ProjectId, true, async identity => Ok(await recipeRecommendationDecisions
            .LinkActualExecutionAsync(decisionId, request, identity.UserId, ct).ConfigureAwait(false)), ct);
    }

    [HttpPost("recipe-recommendation-decisions/{decisionId:guid}/materialize-outcome")]
    public async Task<IActionResult> MaterializeRecipeRecommendationOutcome(Guid decisionId, CancellationToken ct)
    {
        var decision = await store.GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false);
        if (decision is null) return ResourceNotFound("日常配方建议决策不存在。");
        return await ExecuteForProjectAsync(decision.ProjectId, true, async identity => Ok(await recipeRecommendationDecisions
            .MaterializeOutcomeAsync(decisionId, identity.UserId, ct).ConfigureAwait(false)), ct);
    }

    [HttpGet("{projectId:guid}/optimization-readiness")]
    public Task<IActionResult> GetOptimizationReadiness(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(projectId, false, async _ =>
        {
            var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
                ?? throw new ProcessResearchRuleException("优化范围不存在。");
            var assembly = await observationAssembler.AssembleProductionRunsAsync(project, ct).ConfigureAwait(false);
            return Ok(new { assembly.CandidateRunCount, assembly.ValidObservationCount,
                excludedObservationCount = assembly.Observations.Count - assembly.ValidObservationCount,
                observedExecutionKeys = assembly.Observations.Select(static value => value.ExecutionKey).ToArray() });
        }, ct);

    private async Task<IActionResult> ExecuteForProjectAsync(Guid projectId, bool requireWrite,
        Func<PlatformIdentity, Task<IActionResult>> operation, CancellationToken ct)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null) return identity.Result;
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null) return ResourceNotFound("研发项目不存在。");
        if (!CanAccess(project, identity.Identity!, requireWrite)) return AuthorizationDenied();
        return await ExecuteRuleAsync(() => operation(identity.Identity!)).ConfigureAwait(false);
    }

    private Task<IActionResult> ExecuteResearchPageAsync<T>(Guid projectId, string? cursor, int limit,
        Func<string?, Task<ResearchPage<T>>> query, CancellationToken ct)
    {
        cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();
        if (limit is < 1 or > 200) return Task.FromResult<IActionResult>(InvalidRequest("Limit 必须在 1 到 200 之间。"));
        if (cursor is not null && !ResearchPageCursor.TryDecode(cursor, out _, out _))
            return Task.FromResult<IActionResult>(InvalidRequest("分页游标无效或已经损坏。"));
        return ExecuteForProjectAsync(projectId, false, async _ => Ok(await query(cursor).ConfigureAwait(false)), ct);
    }

    private (PlatformIdentity? Identity, IActionResult? Result) ResolveResearchIdentity()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null) return (null, AuthenticationRequired("需要平台统一认证。"));
        return !identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? (null, AuthorizationDenied()) : (identity, null);
    }

    private static bool CanAccess(ResearchProject project, PlatformIdentity identity, bool requireWrite)
    {
        if (requireWrite && project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            return false;
        if (identity.HasAnyRole(PlatformRoles.PlatformAdministrator)) return true;
        return (string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
            project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal)) &&
            !string.IsNullOrWhiteSpace(project.SiteCode) && identity.CanAccessSite(project.SiteCode);
    }

    private async Task<IActionResult> ExecuteRuleAsync(Func<Task<IActionResult>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ProcessResearchRuleException exception) { return StateConflict(exception.Message); }
        catch (ProcessOptimizerUnavailableException exception) { return ServiceUnavailable(exception.Message); }
    }
}

public sealed record ResearchStatusChangeRequest(string TargetStatus, int? Revision = null);
public sealed record ResearchProjectMembersUpdateRequest(int Revision, IReadOnlyList<string> MemberUserIds);
