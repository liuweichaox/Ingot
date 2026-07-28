using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/research-projects")]
public sealed class ResearchProjectsController(
    IProcessResearchStore store,
    ProcessResearchWorkflow workflow,
    ResearchExperimentOptimizer experimentOptimizer,
    IResearchObservationAssembler observationAssembler,
    ICycleComparisonService cycleComparisonService,
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

    [HttpPost("{projectId:guid}/hypotheses/from-cycle-comparison")]
    public Task<IActionResult> ProposeHypothesesFromCycleComparison(
        Guid projectId,
        [FromBody] ResearchHypothesisFromCycleComparisonRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity =>
            {
                var baselineCycleId = request.BaselineCycleId?.Trim();
                var cycleIds = request.CycleIds
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (string.IsNullOrWhiteSpace(baselineCycleId) || cycleIds.Length < 2 ||
                    !cycleIds.Contains(baselineCycleId, StringComparer.Ordinal) ||
                    request.MaximumHypotheses is < 1 or > 10)
                {
                    throw new ProcessResearchRuleException(
                        "请选择包含基准周期的至少两个周期，并指定 1 到 10 条候选假设。");
                }
                var comparison = await cycleComparisonService.CompareSelectedAsync(
                    baselineCycleId,
                    cycleIds,
                    ct).ConfigureAwait(false)
                    ?? throw new ProcessResearchRuleException("所选周期不存在，无法形成追因证据。");
                var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
                    ?? throw new ProcessResearchRuleException("研发项目不存在。");
                var contentHash = Convert.ToHexStringLower(SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(comparison)));
                var evidence = new EvidenceReference
                {
                    EvidenceId = Guid.CreateVersion7(),
                    ProjectId = projectId,
                    Kind = EvidenceKinds.CycleComparison,
                    ReferenceId = $"{comparison.BaselineCycleId}:{contentHash[..16]}",
                    Summary = $"周期比较：{comparison.BaselineCycleId} 与 {comparison.HistoricalCycles.Count} 条历史周期。",
                    ContentHash = contentHash,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                var candidates = comparison.QualityAssociations
                    .Where(static value => value.EvidenceLevel is "stable" or "exploratory")
                    .OrderByDescending(static value => value.CandidateScore)
                    .Take(request.MaximumHypotheses)
                    .ToArray();
                if (candidates.Length == 0)
                    throw new ProcessResearchRuleException("比较结果证据不足，不能自动提出候选原因。");
                var knownVariables = project.Variables.Select(static value => value.Code)
                    .ToHashSet(StringComparer.Ordinal);
                var created = new List<ResearchHypothesis>();
                foreach (var candidate in candidates)
                {
                    var variableCodes = new[] { candidate.SignalCode, candidate.FeatureCode }
                        .Where(knownVariables.Contains)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    created.Add(await workflow.SaveHypothesisAsync(
                        projectId,
                        new ResearchHypothesis
                        {
                            Statement = $"{candidate.SignalCode}.{candidate.FeatureCode} 的过程差异可能影响项目质量目标。",
                            Rationale = $"周期比较给出 {candidate.EvidenceLevel} 证据，候选分数 {candidate.CandidateScore:F3}；通过受控实验验证，不能将该关联直接当作因果结论。",
                            VariableCodes = variableCodes,
                            PossibleConfounders = candidate.PossibleConfounders,
                            Confidence = candidate.EvidenceLevel == "stable" ? 0.65 : 0.35,
                            SupportingEvidence = [evidence with { EvidenceId = Guid.CreateVersion7() }],
                            Applicability = $"产品系列：{comparison.ProductSeries}；分析范围：{comparison.AnalysisScope}。"
                        },
                        identity.UserId,
                        ct).ConfigureAwait(false));
                }
                return Ok(created);
            },
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

    [HttpPost("{projectId:guid}/optimize")]
    public Task<IActionResult> CreateOptimizedExperiment(
        Guid projectId,
        [FromBody] ResearchOptimizationRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await experimentOptimizer.CreateNextExperimentAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpGet("{projectId:guid}/experiment-readiness")]
    public Task<IActionResult> GetExperimentReadiness(
        Guid projectId,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async _ =>
            {
                var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
                    ?? throw new ProcessResearchRuleException("研发项目不存在。");
                var experiments = await store.ListExperimentsAsync(projectId, ct)
                    .ConfigureAwait(false);
                var assembly = await observationAssembler.AssembleAsync(
                    project, experiments, ct).ConfigureAwait(false);
                return Ok(new
                {
                    assembly.CandidateRunCount,
                    assembly.ValidObservationCount,
                    excludedObservationCount =
                        assembly.Observations.Count - assembly.ValidObservationCount,
                    observations = assembly.Observations
                });
            },
            ct);

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
        catch (ProcessOptimizerUnavailableException exception)
        {
            return new ObjectResult(new { error = exception.Message })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
        }
    }
}

public sealed record ResearchStatusChangeRequest(string TargetStatus);
