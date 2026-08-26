// 管理黄金问题及评测，并阻止使用越权 Agent 运行作为评测证据。
using Ingot.Contracts.Agents;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Insight;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/golden-questions")]
public sealed class GoldenQuestionsController(
    PlatformUserResolver userResolver,
    GoldenQuestionApplication application) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new
        {
            data = await application.ListAsync(status, ct).ConfigureAwait(false)
        });

    [HttpGet("{caseId:guid}/{version:int}")]
    public async Task<IActionResult> Get(Guid caseId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await application.GetAsync(caseId, version, ct).ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> SaveDraft([FromBody] GoldenQuestionCase? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (!GoldenQuestionValidator.TryValidate(
                request is null ? null : request with
                {
                    Status = GoldenQuestionStatuses.Draft,
                    ReviewedBy = null,
                    ReviewedAt = null
                },
                false,
                out var normalized,
                out var error))
            return InvalidRequest(error);

        var existing = await application.GetAsync(normalized!.CaseId, normalized.Version, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var value = normalized with
        {
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        try
        {
            return Ok(await application.SaveAsync(value, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("{caseId:guid}/{version:int}:review")]
    public async Task<IActionResult> Review(Guid caseId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await application.GetAsync(caseId, version, ct).ConfigureAwait(false);
        if (existing is null) return ResourceNotFound();
        if (existing.Status == GoldenQuestionStatuses.Reviewed) return Ok(existing);
        if (existing.Status != GoldenQuestionStatuses.Draft)
            return StateConflict("只有草稿黄金问题可以审核。");
        if (!GoldenQuestionValidator.TryValidate(existing, true, out var normalized, out var error))
            return InvalidRequest(error);

        var reviewed = normalized! with
        {
            Status = GoldenQuestionStatuses.Reviewed,
            ReviewedBy = ResolveUserId() ?? "operator",
            ReviewedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Ok(await application.SaveAsync(reviewed, ct).ConfigureAwait(false));
    }

    [HttpPost("{caseId:guid}/{version:int}:evaluate")]
    public async Task<IActionResult> Evaluate(
        Guid caseId,
        int version,
        [FromBody] GoldenEvaluationRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        if (request is null || string.IsNullOrWhiteSpace(request.AgentRunId))
            return InvalidRequest("agentRunId 不能为空。");
        var goldenCase = await application.GetAsync(caseId, version, ct).ConfigureAwait(false);
        if (goldenCase is null) return ResourceNotFound();
        var identity = ResolveIdentity()!;
        try
        {
            var result = await application.EvaluateAsync(
                goldenCase,
                request.AgentRunId.Trim(),
                identity.UserId,
                identity.HasAnyRole(PlatformRoles.PlatformAdministrator),
                identity.SiteIds,
                ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return AuthorizationDenied();
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

    [HttpGet("evaluations")]
    public async Task<IActionResult> Evaluations(
        [FromQuery] Guid? caseId,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var values = await application.ListEvaluationsAsync(caseId, limit, ct).ConfigureAwait(false);
        return Ok(new { data = values, summary = Summarize(values) });
    }

    internal static GoldenEvaluationSummary Summarize(IReadOnlyList<GoldenQuestionEvaluation> values)
    {
        double GateRate(string prefix)
        {
            var gates = values.SelectMany(static value => value.Gates)
                .Where(gate => gate.Code.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            return gates.Length == 0 ? 0 : gates.Count(static gate => gate.Passed) / (double)gates.Length;
        }

        return new GoldenEvaluationSummary
        {
            EvaluationCount = values.Count,
            PassedCount = values.Count(static value => value.Passed),
            PassRate = values.Count == 0 ? 0 : values.Count(static value => value.Passed) / (double)values.Count,
            FactGatePassRate = GateRate("fact."),
            ReferenceGatePassRate = GateRate("reference."),
            RefusalGatePassRate = GateRate("refusal."),
            CausalGuardGatePassRate = GateRate("causal-guard")
        };
    }
}
