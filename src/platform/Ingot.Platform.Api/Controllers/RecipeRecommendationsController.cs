// 暴露按站点与配方生成下一配方建议、登记工程师决定、关联真实运行并冻结结果的 API。
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/recipe-recommendations")]
public sealed class RecipeRecommendationsController(
    RecipeRecommendationQueries queries,
    ResearchOptimizationService optimizationService,
    ResearchRecipeRecommendationDecisionService decisions,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpPost("readiness")]
    public Task<IActionResult> GetReadiness([FromBody] RecipeRecommendationBrief? brief, CancellationToken ct)
        => ExecuteForBriefAsync(brief, async siteBrief =>
            Ok(await optimizationService.GetReadinessAsync(siteBrief, ct).ConfigureAwait(false)));

    [HttpPost]
    public Task<IActionResult> Create([FromBody] ResearchRecipeRecommendationRequest? request, CancellationToken ct)
        => request is null
            ? Task.FromResult<IActionResult>(InvalidRequest("请求体不能为空。"))
            : ExecuteForBriefAsync(request.Brief, async (siteBrief, identity) =>
                Ok(await optimizationService.CreateNextRecipeRecommendationAsync(
                    request with { Brief = siteBrief }, identity.UserId, ct).ConfigureAwait(false)));

    [HttpGet("flows")]
    public async Task<IActionResult> ListFlows(
        [FromQuery] string? siteId,
        [FromQuery] string? processSpecificationId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var identity = ResolveIdentity();
        if (identity.Result is not null) return identity.Result;
        cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();
        if (limit is < 1 or > 200) return InvalidRequest("Limit 必须在 1 到 200 之间。");
        if (cursor is not null && !ResearchPageCursor.TryDecode(cursor, out _, out _))
            return InvalidRequest("分页游标无效或已经损坏。");
        IReadOnlyCollection<string>? siteCodes;
        if (!string.IsNullOrWhiteSpace(siteId))
        {
            var failure = PlatformSiteScope.Resolve(identity.Identity!, siteId, false, out var resolved);
            if (failure == SiteScopeFailure.Forbidden) return AuthorizationDenied();
            if (failure == SiteScopeFailure.Missing || resolved is null) return InvalidRequest("站点无效。");
            siteCodes = [resolved];
        }
        else
        {
            siteCodes = identity.Identity!.HasAnyRole(PlatformRoles.PlatformAdministrator)
                ? null
                : identity.Identity.SiteIds.ToArray();
        }
        var filter = new RecipeRecommendationFilter(
            siteCodes,
            string.IsNullOrWhiteSpace(processSpecificationId) ? null : processSpecificationId.Trim());
        return await ExecuteRuleAsync(async () =>
            Ok(await queries.ListFlowsAsync(filter, cursor, limit, ct).ConfigureAwait(false))).ConfigureAwait(false);
    }

    [HttpPost("{recommendationId:guid}/items/{recommendationKey}/decision")]
    public async Task<IActionResult> RecordDecision(
        Guid recommendationId,
        string recommendationKey,
        [FromBody] ResearchRecipeRecommendationDecisionRequest? request,
        CancellationToken ct)
    {
        if (request is null) return InvalidRequest("请求体不能为空。");
        var recommendation = await queries.GetRecommendationAsync(recommendationId, ct).ConfigureAwait(false);
        if (recommendation is null) return ResourceNotFound("下一配方建议不存在。");
        return await ExecuteForSiteAsync(recommendation.SiteCode, async identity => Ok(await decisions
            .RecordDecisionAsync(recommendationId, recommendationKey, request, identity.UserId, ct)
            .ConfigureAwait(false))).ConfigureAwait(false);
    }

    [HttpPost("decisions/{decisionId:guid}/execution-link")]
    public async Task<IActionResult> LinkExecution(
        Guid decisionId,
        [FromBody] ResearchRecipeRecommendationExecutionLinkRequest? request,
        CancellationToken ct)
    {
        if (request is null) return InvalidRequest("请求体不能为空。");
        var decision = await queries.GetDecisionAsync(decisionId, ct).ConfigureAwait(false);
        if (decision is null) return ResourceNotFound("配方建议决定不存在。");
        return await ExecuteForSiteAsync(decision.SiteCode, async identity => Ok(await decisions
            .LinkActualExecutionAsync(decisionId, request, identity.UserId, ct)
            .ConfigureAwait(false))).ConfigureAwait(false);
    }

    [HttpPost("decisions/{decisionId:guid}/materialize-outcome")]
    public async Task<IActionResult> MaterializeOutcome(Guid decisionId, CancellationToken ct)
    {
        var decision = await queries.GetDecisionAsync(decisionId, ct).ConfigureAwait(false);
        if (decision is null) return ResourceNotFound("配方建议决定不存在。");
        return await ExecuteForSiteAsync(decision.SiteCode, async identity => Ok(await decisions
            .MaterializeOutcomeAsync(decisionId, identity.UserId, ct)
            .ConfigureAwait(false))).ConfigureAwait(false);
    }

    private Task<IActionResult> ExecuteForBriefAsync(
        RecipeRecommendationBrief? brief,
        Func<RecipeRecommendationBrief, Task<IActionResult>> operation)
        => ExecuteForBriefAsync(brief, (siteBrief, _) => operation(siteBrief));

    private async Task<IActionResult> ExecuteForBriefAsync(
        RecipeRecommendationBrief? brief,
        Func<RecipeRecommendationBrief, PlatformIdentity, Task<IActionResult>> operation)
    {
        if (brief is null) return InvalidRequest("必须提供建议条件。");
        var identity = ResolveIdentity();
        if (identity.Result is not null) return identity.Result;
        var failure = PlatformSiteScope.Resolve(identity.Identity!, brief.SiteCode, false, out var siteId);
        if (failure == SiteScopeFailure.Forbidden) return AuthorizationDenied();
        if (failure == SiteScopeFailure.Missing || siteId is null) return InvalidRequest("建议条件必须指定站点。");
        return await ExecuteRuleAsync(() => operation(brief with { SiteCode = siteId }, identity.Identity!))
            .ConfigureAwait(false);
    }

    private async Task<IActionResult> ExecuteForSiteAsync(
        string siteCode,
        Func<PlatformIdentity, Task<IActionResult>> operation)
    {
        var identity = ResolveIdentity();
        if (identity.Result is not null) return identity.Result;
        if (!identity.Identity!.HasAnyRole(PlatformRoles.PlatformAdministrator) &&
            !identity.Identity.CanAccessSite(siteCode))
            return AuthorizationDenied();
        return await ExecuteRuleAsync(() => operation(identity.Identity)).ConfigureAwait(false);
    }

    private (PlatformIdentity? Identity, IActionResult? Result) ResolveIdentity()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null) return (null, AuthenticationRequired("需要平台统一认证。"));
        return !identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? (null, AuthorizationDenied())
            : (identity, null);
    }

    private async Task<IActionResult> ExecuteRuleAsync(Func<Task<IActionResult>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ProcessResearchRuleException exception) { return StateConflict(exception.Message); }
        catch (ProcessOptimizerUnavailableException exception) { return ServiceUnavailable(exception.Message); }
    }
}
