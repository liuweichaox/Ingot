using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Insight;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/golden-questions")]
public sealed class GoldenQuestionsController(
    PlatformUserResolver userResolver,
    IGoldenQuestionStore store,
    IAgentRunStore agentRuns,
    GoldenQuestionEvaluator evaluator) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new
        {
            data = await store.ListAsync(status, ct).ConfigureAwait(false)
        });

    [HttpGet("{caseId:guid}/{version:int}")]
    public async Task<IActionResult> Get(Guid caseId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetAsync(caseId, version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
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
            return BadRequest(new { error });

        var existing = await store.GetAsync(normalized!.CaseId, normalized.Version, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var value = normalized with
        {
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        try
        {
            return Ok(await store.SaveAsync(value, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("{caseId:guid}/{version:int}:review")]
    public async Task<IActionResult> Review(Guid caseId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await store.GetAsync(caseId, version, ct).ConfigureAwait(false);
        if (existing is null) return NotFound();
        if (existing.Status == GoldenQuestionStatuses.Reviewed) return Ok(existing);
        if (existing.Status != GoldenQuestionStatuses.Draft)
            return Conflict(new { error = "只有草稿黄金问题可以审核。" });
        if (!GoldenQuestionValidator.TryValidate(existing, true, out var normalized, out var error))
            return BadRequest(new { error });

        var reviewed = normalized! with
        {
            Status = GoldenQuestionStatuses.Reviewed,
            ReviewedBy = ResolveUserId() ?? "operator",
            ReviewedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return Ok(await store.SaveAsync(reviewed, ct).ConfigureAwait(false));
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
            return BadRequest(new { error = "agentRunId 不能为空。" });
        var goldenCase = await store.GetAsync(caseId, version, ct).ConfigureAwait(false);
        if (goldenCase is null) return NotFound();
        var run = await agentRuns.GetAsync(request.AgentRunId.Trim(), ct).ConfigureAwait(false);
        if (run is null) return BadRequest(new { error = "指定 Agent 运行不存在。" });
        try
        {
            var result = evaluator.Evaluate(goldenCase, run);
            await store.SaveEvaluationAsync(result, ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { error = exception.Message });
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
        var values = await store.ListEvaluationsAsync(caseId, limit, ct).ConfigureAwait(false);
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
