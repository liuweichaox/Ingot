using Ingot.Platform.Api.Agents;
using Ingot.Contracts.Insight;
using Ingot.Platform.Infrastructure.Insight;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

/// <summary>
///     问题档案：证据定级主轴。读取需质量只读角色；创建/编辑/核定需工艺工程师或平台管理员。
///     定级为只读分析，任何读取角色可触发；等级由数据自动评定并诚实降级。
/// </summary>
[ApiController]
[Route("api/v1/problem-cases")]
public sealed class ProblemCasesController(
    PlatformUserResolver userResolver,
    IProblemCaseStore store,
    CaseLevelEvaluator evaluator) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListAsync(status, ct).ConfigureAwait(false) });

    [HttpGet("{caseId:guid}")]
    public async Task<IActionResult> Get(Guid caseId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetAsync(caseId, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ProblemCaseUpsertRequest? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null || string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "title 不能为空。" });
        if (request.Scope is null)
            return BadRequest(new { error = "scope 不能为空。" });

        var now = DateTimeOffset.UtcNow;
        var existing = request.CaseId is { } id ? await store.GetAsync(id, ct).ConfigureAwait(false) : null;
        var problemCase = new ProblemCase
        {
            CaseId = existing?.CaseId ?? request.CaseId ?? Guid.CreateVersion7(),
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? existing?.Description ?? string.Empty,
            Status = string.IsNullOrWhiteSpace(request.Status) ? existing?.Status ?? "open" : request.Status.Trim(),
            Scope = NormalizeScope(request.Scope),
            TargetMetric = request.TargetMetric?.Trim() ?? existing?.TargetMetric ?? string.Empty,
            // 等级与人工核定标志不经此入口修改：分别由 :evaluate 与 :ratify 维护。
            CurrentLevel = existing?.CurrentLevel ?? CaseLevels.L0Pending,
            FeatureSetRatified = existing?.FeatureSetRatified ?? false,
            RatifiedBy = existing?.RatifiedBy,
            RatifiedAt = existing?.RatifiedAt,
            Owner = request.Owner?.Trim() ?? existing?.Owner ?? ResolveUserId(),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        return Ok(await store.UpsertAsync(problemCase, ct).ConfigureAwait(false));
    }

    [HttpPost("{caseId:guid}:evaluate")]
    public async Task<IActionResult> Evaluate(Guid caseId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var problemCase = await store.GetAsync(caseId, ct).ConfigureAwait(false);
        if (problemCase is null) return NotFound();

        var evaluation = await evaluator.EvaluateAsync(problemCase, ct).ConfigureAwait(false);
        await store.SaveEvaluationAsync(evaluation, ct).ConfigureAwait(false);
        if (!string.Equals(problemCase.CurrentLevel, evaluation.Level, StringComparison.Ordinal))
            await store.UpdateLevelAsync(caseId, evaluation.Level, ct).ConfigureAwait(false);
        return Ok(evaluation);
    }

    [HttpPost("{caseId:guid}:ratify")]
    public async Task<IActionResult> Ratify(Guid caseId, [FromBody] RatifyRequest? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var userId = ResolveUserId() ?? "unknown";
        var updated = await store.SetRatifiedAsync(caseId, request?.Ratified ?? true, userId, ct).ConfigureAwait(false);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("{caseId:guid}/evaluations")]
    public async Task<IActionResult> Evaluations(Guid caseId, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        return Ok(new { data = await store.ListEvaluationsAsync(caseId, limit, ct).ConfigureAwait(false) });
    }

    private static CaseScope NormalizeScope(CaseScope scope) => new()
    {
        SubjectType = string.IsNullOrWhiteSpace(scope.SubjectType) ? null : scope.SubjectType.Trim(),
        SubjectId = string.IsNullOrWhiteSpace(scope.SubjectId) ? null : scope.SubjectId.Trim(),
        ContextFilter = scope.ContextFilter
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(static pair => pair.Key.Trim(), static pair => pair.Value.Trim(), StringComparer.Ordinal),
        ComparisonKey = string.IsNullOrWhiteSpace(scope.ComparisonKey) ? null : scope.ComparisonKey.Trim(),
        WindowFrom = scope.WindowFrom,
        WindowTo = scope.WindowTo
    };

    public sealed record RatifyRequest
    {
        public bool Ratified { get; init; } = true;
    }
}
