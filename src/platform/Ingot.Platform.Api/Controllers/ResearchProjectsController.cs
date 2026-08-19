using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/research-projects")]
public sealed class ResearchProjectsController(
    IProcessResearchStore store,
    ProcessResearchWorkflow workflow,
    ResearchExperimentDesignService experimentDesigns,
    ResearchExperimentValidationService experimentValidation,
    ResearchExperimentOptimizer experimentOptimizer,
    ResearchShadowRecommendationService shadowRecommendations,
    ResearchHistoricalReplayService historicalReplay,
    ResearchOnlineAdmissionService onlineAdmission,
    ResearchRollbackDrillService rollbackDrills,
    ResearchOnlineCampaignService onlineCampaign,
    ResearchTransferAssessmentService transferAssessments,
    ResearchValidationPreregistrationService validationPreregistrations,
    IResearchObservationAssembler observationAssembler,
    ResearchExperimentResultMaterializer resultMaterializer,
    IExecutionComparisonService executionComparisonService,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet("{projectId:guid}/stage-zero-admission")]
    public Task<IActionResult> GetStageZeroAdmission(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await validationPreregistrations.AssessAsync(projectId, ct)
                .ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/validation-preregistrations")]
    public Task<IActionResult> FreezeValidationPreregistration(
        Guid projectId,
        [FromBody] ResearchValidationPreregistrationRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await validationPreregistrations.FreezeAsync(
                projectId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct);

    [HttpPost("validation-preregistrations/{preregistrationId:guid}/review")]
    public async Task<IActionResult> ReviewValidationPreregistration(
        Guid preregistrationId,
        CancellationToken ct)
    {
        var value = await store.GetValidationPreregistrationAsync(preregistrationId, ct)
            .ConfigureAwait(false);
        if (value is null)
            return NotFound(new { error = "阶段 0 预注册不存在。" });
        return await ExecuteForProjectAsync(
            value.ProjectId,
            true,
            async identity => Ok(await validationPreregistrations.ReviewAsync(
                preregistrationId, identity.UserId, ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

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
            async _ =>
            {
                var workspace = await workflow.GetWorkspaceAsync(projectId, ct).ConfigureAwait(false);
                var report = await shadowRecommendations.BuildReportAsync(projectId, ct)
                    .ConfigureAwait(false);
                var onlineReport = await onlineCampaign.BuildReportAsync(projectId, ct)
                    .ConfigureAwait(false);
                return Ok(workspace with { ShadowReport = report, OnlineReport = onlineReport });
            },
            ct).ConfigureAwait(false);

    [HttpGet("{projectId:guid}/shadow-report")]
    public Task<IActionResult> GetShadowReport(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await shadowRecommendations.BuildReportAsync(projectId, ct)
                .ConfigureAwait(false)),
            ct);

    [HttpGet("{projectId:guid}/online-admission")]
    public Task<IActionResult> GetOnlineAdmission(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await onlineAdmission.AssessAsync(projectId, ct).ConfigureAwait(false)),
            ct);

    [HttpGet("{projectId:guid}/online-report")]
    public Task<IActionResult> GetOnlineReport(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await onlineCampaign.BuildReportAsync(projectId, ct).ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/historical-replays")]
    public Task<IActionResult> RunHistoricalReplay(
        Guid projectId,
        [FromBody] ResearchHistoricalReplayRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await historicalReplay.RunAsync(
                projectId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct);

    [HttpPost("historical-replays/{reportId:guid}/review")]
    public async Task<IActionResult> ReviewHistoricalReplay(
        Guid reportId,
        CancellationToken ct)
    {
        var report = await store.GetHistoricalReplayReportAsync(reportId, ct)
            .ConfigureAwait(false);
        if (report is null)
            return NotFound(new { error = "历史回放报告不存在。" });
        return await ExecuteForProjectAsync(
            report.ProjectId,
            true,
            async identity => Ok(await historicalReplay.ReviewAsync(
                reportId, identity.UserId, ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("{projectId:guid}/rollback-drills")]
    public Task<IActionResult> RecordRollbackDrill(
        Guid projectId,
        [FromBody] ResearchRollbackDrillRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await rollbackDrills.RecordAsync(
                projectId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct);

    [HttpPost("rollback-drills/{drillId:guid}/review")]
    public async Task<IActionResult> ReviewRollbackDrill(
        Guid drillId,
        CancellationToken ct)
    {
        var drill = await store.GetRollbackDrillAsync(drillId, ct).ConfigureAwait(false);
        if (drill is null)
            return NotFound(new { error = "停止与回退演练不存在。" });
        return await ExecuteForProjectAsync(
            drill.ProjectId,
            true,
            async identity => Ok(await rollbackDrills.ReviewAsync(
                drillId, identity.UserId, ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

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

    [HttpPost("{projectId:guid}/hypotheses/from-execution-comparison")]
    public Task<IActionResult> ProposeHypothesesFromExecutionComparison(
        Guid projectId,
        [FromBody] ResearchHypothesisFromExecutionComparisonRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity =>
            {
                var baselineProcessExecutionId = request.BaselineProcessExecutionId?.Trim();
                var executionIds = request.ProcessExecutionIds
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (string.IsNullOrWhiteSpace(baselineProcessExecutionId) || executionIds.Length < 2 ||
                    !executionIds.Contains(baselineProcessExecutionId, StringComparer.Ordinal) ||
                    request.MaximumHypotheses is < 1 or > 10)
                {
                    throw new ProcessResearchRuleException(
                        "请选择包含基准过程执行的至少两个过程执行，并指定 1 到 10 条候选假设。");
                }
                var comparison = await executionComparisonService.CompareSelectedAsync(
                    baselineProcessExecutionId,
                    executionIds,
                    ct).ConfigureAwait(false)
                    ?? throw new ProcessResearchRuleException("所选过程执行不存在，无法形成追因证据。");
                var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
                    ?? throw new ProcessResearchRuleException("研发项目不存在。");
                var contentHash = Convert.ToHexStringLower(SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(comparison)));
                var evidence = new EvidenceReference
                {
                    EvidenceId = Guid.CreateVersion7(),
                    ProjectId = projectId,
                    Kind = EvidenceKinds.ExecutionComparison,
                    ReferenceId = $"{comparison.BaselineProcessExecutionId}:{contentHash[..16]}",
                    Summary = $"过程执行比较：{comparison.BaselineProcessExecutionId} 与 {comparison.HistoricalProcessExecutions.Count} 条历史过程执行。",
                    ContentHash = contentHash,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                var candidates = comparison.Diagnosis.Candidates
                    .Where(static value => value.EvidenceLevel is "stable" or "exploratory")
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        VariableCodes = ResolveControllableVariables(project, candidate)
                    })
                    .Where(static value => value.VariableCodes.Count > 0)
                    .OrderByDescending(static value => value.Candidate.CandidateScore)
                    .Take(request.MaximumHypotheses)
                    .ToArray();
                if (candidates.Length == 0)
                {
                    throw new ProcessResearchRuleException(
                        "比较结果没有与项目可控变量数据来源匹配的候选原因；请检查项目变量的实际数据来源映射。");
                }
                var validationObjective = project.Objectives
                    .OrderByDescending(static objective => objective.Weight)
                    .ThenBy(static objective => objective.Code, StringComparer.Ordinal)
                    .FirstOrDefault();
                var created = new List<ResearchHypothesis>();
                foreach (var resolved in candidates)
                {
                    var candidate = resolved.Candidate;
                    var sourceLabel = candidate.SourceKind == ExecutionCauseSourceKinds.ProcessSpecificationParameter
                        ? "实际控制参数"
                        : "过程轨迹特征";
                    var direction = candidate.MedianDifference is > 0
                        ? "不合格组更高"
                        : candidate.MedianDifference is < 0 ? "不合格组更低" : "组间存在差异";
                    var confoundingPenalty = candidate.PossibleConfounders.Count == 0 ? 0d : 0.15d;
                    created.Add(await workflow.SaveHypothesisAsync(
                        projectId,
                        new ResearchHypothesis
                        {
                            Statement = $"{candidate.DisplayName} 的差异可能影响项目质量目标。",
                            Rationale =
                                $"{sourceLabel}在合格与不合格过程执行间表现为“{direction}”，" +
                                $"诊断证据为 {candidate.EvidenceLevel}，候选分数 {candidate.CandidateScore:F3}。" +
                                "该结论只是观察性关联，必须通过受控实验验证。",
                            VariableCodes = resolved.VariableCodes,
                            ValidationOutcomeCode = validationObjective?.Code,
                            ExpectedEffectDirection = validationObjective is null
                                ? null
                                : ResolveValidationDirection(validationObjective),
                            MinimumEffect = validationObjective is null
                                ? null
                                : ResolveMinimumEffect(validationObjective),
                            PossibleConfounders = candidate.PossibleConfounders,
                            Confidence = Math.Max(
                                0.2d,
                                (candidate.EvidenceLevel == "stable" ? 0.65d : 0.4d) -
                                confoundingPenalty),
                            SupportingEvidence = [evidence with { EvidenceId = Guid.CreateVersion7() }],
                            FalsificationConditions =
                            [
                                "后续同条件重复比较或受控实验未再观察到该变量差异与结果差异同向出现。"
                            ],
                            Applicability =
                                $"产品系列：{comparison.ProductFamilyCode}；分析范围：{comparison.AnalysisScope}；" +
                                $"数据来源：{candidate.DataSource}。"
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

    [HttpPost("{projectId:guid}/experiment-designs/preview")]
    public Task<IActionResult> PreviewExperimentDesign(
        Guid projectId,
        [FromBody] ResearchExperimentDesignRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async _ => Ok(await experimentDesigns.PreviewAsync(projectId, request, ct)
                .ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/experiments/validate")]
    public Task<IActionResult> ValidateExperiment(
        Guid projectId,
        [FromBody] ResearchExperiment request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async _ => Ok(await experimentValidation.ValidateAsync(projectId, request, ct)
                .ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/experiments/import-history")]
    public Task<IActionResult> ImportHistoricalRuns(
        Guid projectId,
        [FromBody] ResearchHistoricalRunImportRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity =>
            {
                var executionIds = request.ProcessExecutionIds
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .Take(2000)
                    .ToArray();
                if (executionIds.Length < 2)
                    throw new ProcessResearchRuleException("至少选择两个已完成运行，才能作为历史实验观察。");
                if (request.ProcessExecutionIds.Count > executionIds.Length && request.ProcessExecutionIds.Count > 2000)
                    throw new ProcessResearchRuleException("一次最多导入 2000 个历史运行。");

                var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
                    ?? throw new ProcessResearchRuleException("研发项目不存在。");
                var controls = project.Variables
                    .Where(static value => value.Role == ResearchVariableRoles.Control)
                    .ToArray();
                if (controls.Length == 0)
                    throw new ProcessResearchRuleException("项目没有定义可控变量，不能导入历史运行。");

                var resolvedExecutions = await executionComparisonService
                    .GetProcessExecutionsAsync(executionIds, ct)
                    .ConfigureAwait(false);
                var executions = new List<ExecutionComparisonRow>(executionIds.Length);
                foreach (var executionId in executionIds)
                {
                    if (!resolvedExecutions.TryGetValue(executionId, out var execution))
                        throw new ProcessResearchRuleException($"运行 {executionId} 不存在。");
                    if (execution.CompletedAt is null)
                        throw new ProcessResearchRuleException($"运行 {executionId} 尚未完成，不能作为历史观察。");
                    executions.Add(execution);
                }
                var productFamilyCode = executions[0].ProductFamilyCode;
                if (executions.Any(execution => !string.Equals(
                        execution.ProductFamilyCode, productFamilyCode, StringComparison.Ordinal)))
                {
                    throw new ProcessResearchRuleException("历史运行必须属于同一产品系列，避免把不可比数据混入优化模型。");
                }

                var runs = executions.Select((execution, index) => new ExperimentRunPlan
                {
                    ExecutionKey = execution.ExecutionId,
                    Sequence = index + 1,
                    Factors = controls.Select(variable => new ExperimentFactorSetting
                    {
                        VariableCode = variable.Code,
                        Value = ReadHistoricalProcessSpecificationValue(execution, variable),
                        Unit = variable.Unit
                    }).ToArray()
                }).ToArray();
                var distinctConditions = runs
                    .Select(run => string.Join("|", run.Factors
                        .OrderBy(static factor => factor.VariableCode, StringComparer.Ordinal)
                        .Select(static factor => $"{factor.VariableCode}:{factor.Value:R}")))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                if (distinctConditions < 2)
                    throw new ProcessResearchRuleException(
                        "所选历史运行没有至少两种不同的实际工艺规范条件，不能作为比较实验。请选择包含不同工艺规范水平的运行。");

                var existing = (await store.ListExperimentsAsync(projectId, ct).ConfigureAwait(false))
                    .FirstOrDefault(experiment =>
                        experiment.DesignMethod == ResearchDesignMethods.HistoricalObservation &&
                        experiment.RunPlan.Select(static run => run.ExecutionKey)
                            .OrderBy(static key => key, StringComparer.Ordinal)
                            .SequenceEqual(runs.Select(static run => run.ExecutionKey)
                                .OrderBy(static key => key, StringComparer.Ordinal), StringComparer.Ordinal));
                if (existing is not null)
                    return Ok(existing);

                var experiment = await workflow.CreateExperimentAsync(
                    projectId,
                    new ResearchExperiment
                    {
                        Name = $"历史运行证据集 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}",
                        DesignMethod = ResearchDesignMethods.HistoricalObservation,
                        RunPlan = runs,
                        ObjectiveCodes = project.Objectives.Select(static value => value.Code).ToArray(),
                        StopRule = "仅导入已经完成且数据冻结的历史运行；不据此直接下达生产工艺规范。",
                        RollbackPlan = "历史证据导入不向设备写入任何参数；后续验证实验须经工程师批准。"
                    },
                    identity.UserId,
                    ct).ConfigureAwait(false);
                return Ok(experiment);
            },
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

    [HttpPost("experiments/{experimentId:guid}/clone")]
    public async Task<IActionResult> CloneExperiment(
        Guid experimentId,
        [FromBody] ResearchExperimentCloneRequest request,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return NotFound(new { error = "实验不存在。" });
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await workflow.CloneExperimentAsync(
                experimentId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("experiments/{experimentId:guid}/materialize-result")]
    public async Task<IActionResult> MaterializeExperimentResult(
        Guid experimentId,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return NotFound(new { error = "实验不存在。" });
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity =>
            {
                var workspace = await workflow.GetWorkspaceAsync(experiment.ProjectId, ct)
                    .ConfigureAwait(false);
                var assembly = await observationAssembler.AssembleAsync(
                    workspace.Project, workspace.Experiments, ct).ConfigureAwait(false);
                var materialized = await resultMaterializer.MaterializeCompletedAsync(
                    workspace.Project,
                    workspace.Experiments,
                    workspace.ExperimentResults,
                    assembly,
                    identity.UserId,
                    ct).ConfigureAwait(false);
                var result = materialized.FirstOrDefault(value => value.ExperimentId == experimentId)
                    ?? throw new ProcessResearchRuleException(
                        "尚未找到全部计划运行的完整工艺规范、过程和检验数据，不能自动计算结果。");
                return Ok(result);
            },
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

    [HttpPost("experiments/{experimentId:guid}/runs/{suggestionExecutionKey}/shadow-decision")]
    public async Task<IActionResult> RecordShadowDecision(
        Guid experimentId,
        string suggestionExecutionKey,
        [FromBody] ResearchShadowDecisionRequest request,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return NotFound(new { error = "优化实验不存在。" });
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await shadowRecommendations.RecordDecisionAsync(
                experimentId,
                suggestionExecutionKey,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("experiments/{experimentId:guid}/controlled-decision")]
    public async Task<IActionResult> DecideControlledExperiment(
        Guid experimentId,
        [FromBody] ResearchControlledDecisionRequest request,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return NotFound(new { error = "受控在线建议不存在。" });
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await workflow.DecideControlledExperimentAsync(
                experimentId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("shadow-recommendations/{recommendationId:guid}/materialize-outcome")]
    public async Task<IActionResult> MaterializeShadowOutcome(
        Guid recommendationId,
        CancellationToken ct)
    {
        var recommendation = await store.GetShadowRecommendationAsync(recommendationId, ct)
            .ConfigureAwait(false);
        if (recommendation is null)
            return NotFound(new { error = "影子建议不存在。" });
        return await ExecuteForProjectAsync(
            recommendation.ProjectId,
            true,
            async identity => Ok(await shadowRecommendations.MaterializeOutcomeAsync(
                recommendationId,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

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
                    observedExecutionKeys = assembly.Observations
                        .Select(static observation => observation.ExecutionKey)
                        .ToArray()
                });
            },
            ct);

    [HttpPost("{projectId:guid}/operating-regions")]
    public Task<IActionResult> SaveOperatingRegion(
        Guid projectId,
        [FromBody] ResearchOperatingRegion request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await workflow.SaveOperatingRegionAsync(
                projectId,
                request,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct);

    [HttpPost("operating-regions/{operatingRegionId:guid}/validate")]
    public async Task<IActionResult> ValidateOperatingRegion(Guid operatingRegionId, CancellationToken ct)
    {
        var window = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false);
        if (window is null)
            return NotFound(new { error = "工艺操作域不存在。" });
        return await ExecuteForProjectAsync(
            window.ProjectId,
            true,
            async identity => Ok(await workflow.ValidateOperatingRegionAsync(
                operatingRegionId,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("operating-regions/{operatingRegionId:guid}/design-validation")]
    public async Task<IActionResult> DesignOperatingRegionValidation(
        Guid operatingRegionId,
        CancellationToken ct)
    {
        var window = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false);
        if (window is null)
            return NotFound(new { error = "工艺操作域不存在。" });
        return await ExecuteForProjectAsync(
            window.ProjectId,
            true,
            async identity => Ok(await workflow.CreateOperatingRegionValidationExperimentAsync(
                operatingRegionId,
                identity.UserId,
                ct).ConfigureAwait(false)),
            ct).ConfigureAwait(false);
    }

    [HttpPost("operating-regions/{operatingRegionId:guid}/release")]
    public async Task<IActionResult> ReleaseOperatingRegion(Guid operatingRegionId, CancellationToken ct)
    {
        var window = await store.GetOperatingRegionAsync(operatingRegionId, ct).ConfigureAwait(false);
        if (window is null)
            return NotFound(new { error = "工艺操作域不存在。" });
        return await ExecuteForProjectAsync(
            window.ProjectId,
            true,
            async identity => Ok(await workflow.ReleaseOperatingRegionAsync(
                operatingRegionId,
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

    [HttpPost("{projectId:guid}/transfer-assessments")]
    public Task<IActionResult> AssessTransfer(
        Guid projectId,
        [FromBody] ResearchTransferAssessmentRequest request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity =>
            {
                var window = await store.GetOperatingRegionAsync(request.SourceOperatingRegionId, ct)
                    .ConfigureAwait(false);
                var source = window is null
                    ? null
                    : await store.GetProjectAsync(window.ProjectId, ct).ConfigureAwait(false);
                if (source is not null && !CanAccess(source, identity, false))
                    return Forbid();
                return Ok(await transferAssessments.AssessAsync(
                    projectId,
                    request,
                    identity.UserId,
                    ct).ConfigureAwait(false));
            },
            ct);

    [HttpGet("{projectId:guid}/transfer-sources")]
    public Task<IActionResult> GetTransferSources(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async identity =>
            {
                var projects = await store.ListProjectsAsync(
                    identity.UserId,
                    identity.HasAnyRole(PlatformRoles.PlatformAdministrator),
                    100,
                    0,
                    ct).ConfigureAwait(false);
                var rows = await Task.WhenAll(projects.Select(async project => new
                {
                    project,
                    windows = await store.ListOperatingRegionsAsync(project.ProjectId, ct)
                        .ConfigureAwait(false)
                })).ConfigureAwait(false);
                return Ok(new
                {
                    data = rows.SelectMany(row => row.windows
                        .Where(window => window.Status == OperatingRegionStatuses.Validated &&
                                         window.ValidationLevel == OperatingRegionValidationLevels.Production)
                        .Select(window => new
                        {
                            sourceProjectId = row.project.ProjectId,
                            sourceProjectName = row.project.Name,
                            sourceProcessName = row.project.ProcessName,
                            sourceProductName = row.project.ProductName,
                            sourceMaterialName = row.project.MaterialName,
                            sourceSiteCode = row.project.SiteCode,
                            operatingRegionId = window.OperatingRegionId,
                            operatingRegionName = window.Name,
                            window.Applicability,
                            window.AnalysisHash
                        }))
                });
            },
            ct);

    [HttpPost("transfer-assessments/{assessmentId:guid}/review")]
    public async Task<IActionResult> ReviewTransferAssessment(
        Guid assessmentId,
        CancellationToken ct)
    {
        var assessment = await store.GetTransferAssessmentAsync(assessmentId, ct)
            .ConfigureAwait(false);
        if (assessment is null)
            return NotFound(new { error = "迁移评估不存在。" });
        return await ExecuteForProjectAsync(
            assessment.ProjectId,
            true,
            async identity => Ok(await transferAssessments.ReviewAsync(
                assessmentId,
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

    private static double ReadHistoricalProcessSpecificationValue(ExecutionComparisonRow execution, ResearchVariable variable)
    {
        var source = variable.DataSource?.Trim();
        var controlParameterCode = !string.IsNullOrWhiteSpace(source) &&
                         source.StartsWith("control-parameter:", StringComparison.OrdinalIgnoreCase)
            ? source["control-parameter:".Length..].Trim()
            : variable.Code;
        var value = execution.ControlParameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Code, controlParameterCode, StringComparison.Ordinal));
        if (value is null || !TryReadNumber(value.Value, out var number))
        {
            throw new ProcessResearchRuleException(
                $"运行 {execution.ExecutionId} 缺少可控变量 {variable.Code} 的实际控制参数回读，不能作为优化观察。");
        }
        return number;
    }

    private static bool TryReadNumber(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) &&
            double.IsFinite(number))
            return true;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out number) && double.IsFinite(number))
            return true;
        number = default;
        return false;
    }

    private static IReadOnlyList<string> ResolveControllableVariables(
        ResearchProject project,
        ExecutionCauseCandidate candidate)
        => project.Variables
            .Where(static variable => variable.Role == ResearchVariableRoles.Control)
            .Where(variable =>
            {
                var source = variable.DataSource?.Trim();
                if (!string.IsNullOrWhiteSpace(source))
                {
                    return string.Equals(
                        source,
                        candidate.DataSource,
                        StringComparison.OrdinalIgnoreCase);
                }
                return candidate.SourceKind == ExecutionCauseSourceKinds.ProcessSpecificationParameter &&
                       string.Equals(
                           variable.Code,
                           candidate.VariableCode,
                           StringComparison.Ordinal);
            })
            .Select(static variable => variable.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string ResolveValidationDirection(ResearchObjective objective)
        => objective.Direction.ToLowerInvariant() switch
        {
            "maximize" or "max" or "increase" => ResearchHypothesisEffectDirections.Increase,
            "minimize" or "min" or "decrease" => ResearchHypothesisEffectDirections.Decrease,
            _ when objective.Baseline is { } baseline && baseline < objective.Target =>
                ResearchHypothesisEffectDirections.Increase,
            _ => ResearchHypothesisEffectDirections.Decrease
        };

    private static double ResolveMinimumEffect(ResearchObjective objective)
    {
        if (objective.Baseline is { } baseline &&
            Math.Abs(baseline - objective.Target) > 1e-12)
            return Math.Max(Math.Abs(baseline - objective.Target) * 0.1, 1e-9);
        if (objective.LowerLimit is { } lower && objective.UpperLimit is { } upper &&
            upper > lower)
            return Math.Max((upper - lower) * 0.01, 1e-9);
        return Math.Max(Math.Abs(objective.Target) * 0.01, 0.001);
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
        catch (ResearchExperimentValidationException exception)
        {
            return new ConflictObjectResult(new
            {
                error = exception.Message,
                errors = exception.Errors
            });
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
