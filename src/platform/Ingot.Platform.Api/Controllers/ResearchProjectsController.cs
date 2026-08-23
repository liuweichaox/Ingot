
// 暴露项目级研发工作流 API；业务准入、证据判断和状态转换均委托给 Application 层。
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/research-projects")]
public sealed class ResearchProjectsController(
    ProcessResearchQueries store,
    ProcessResearchWorkflow workflow,
    ResearchExperimentCommands experimentCommands,
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
    ResearchExecutionEvidenceService executionEvidence,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet("{projectId:guid}/experiments")]
    public Task<IActionResult> ListExperiments(
        Guid projectId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListExperimentsPageAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/experiment-results")]
    public Task<IActionResult> ListExperimentResults(
        Guid projectId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListExperimentResultsPageAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/shadow-recommendations")]
    public Task<IActionResult> ListShadowRecommendations(
        Guid projectId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListShadowRecommendationsPageAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/historical-replays")]
    public Task<IActionResult> ListHistoricalReplays(
        Guid projectId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListHistoricalReplayReportsPageAsync(projectId, value, limit, ct), ct);

    [HttpGet("{projectId:guid}/audit")]
    public Task<IActionResult> ListAudit(
        Guid projectId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
        => ExecuteResearchPageAsync(projectId, cursor, limit,
            value => store.ListAuditEntriesPageAsync(projectId, value, limit, ct), ct);

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
            return ResourceNotFound("阶段 0 预注册不存在。");
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

    [HttpGet("{projectId:guid}/method-admission")]
    public Task<IActionResult> GetMethodAdmission(Guid projectId, CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await experimentOptimizer.AssessMethodAdmissionAsync(projectId, ct)
                .ConfigureAwait(false)),
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
            return ResourceNotFound("历史回放报告不存在。");
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
            return ResourceNotFound("停止与回退演练不存在。");
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

    [HttpPatch("{projectId:guid}/members")]
    public async Task<IActionResult> UpdateMembers(
        Guid projectId,
        [FromBody] ResearchProjectMembersUpdateRequest request,
        CancellationToken ct)
    {
        var identity = ResolveResearchIdentity();
        if (identity.Result is not null)
            return identity.Result;
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
            return ResourceNotFound("研发项目不存在。");
        var isAdministrator = identity.Identity!.HasAnyRole(PlatformRoles.PlatformAdministrator);
        if (!isAdministrator &&
            !string.Equals(project.OwnerUserId, identity.Identity.UserId, StringComparison.Ordinal))
            return AuthorizationDenied("只有项目负责人或平台管理员可以管理项目成员。");
        return await ExecuteRuleAsync(async () => Ok(await workflow.UpdateProjectMembersAsync(
            projectId,
            request.Revision,
            request.MemberUserIds,
            identity.Identity.UserId,
            isAdministrator,
            ct).ConfigureAwait(false))).ConfigureAwait(false);
    }

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
            async identity => Ok(await executionEvidence.ProposeHypothesesAsync(
                projectId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct);

    [HttpPost("{projectId:guid}/experiments")]
    public Task<IActionResult> CreateExperiment(
        Guid projectId,
        [FromBody] ResearchExperiment request,
        CancellationToken ct)
        => ExecuteForProjectAsync(
            projectId,
            true,
            async identity => Ok(await experimentCommands.CreateExperimentAsync(
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
            async identity => Ok(await executionEvidence.ImportHistoricalRunsAsync(
                projectId, request, identity.UserId, ct).ConfigureAwait(false)),
            ct);

    [HttpPost("experiments/{experimentId:guid}/status")]
    public async Task<IActionResult> ChangeExperimentStatus(
        Guid experimentId,
        [FromBody] ResearchStatusChangeRequest request,
        CancellationToken ct)
    {
        var experiment = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
        if (experiment is null)
            return ResourceNotFound("实验不存在。");
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await experimentCommands.ChangeExperimentStatusAsync(
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
            return ResourceNotFound("实验不存在。");
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await experimentCommands.CloneExperimentAsync(
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
            return ResourceNotFound("实验不存在。");
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
            return ResourceNotFound("优化实验不存在。");
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
            return ResourceNotFound("受控在线建议不存在。");
        return await ExecuteForProjectAsync(
            experiment.ProjectId,
            true,
            async identity => Ok(await experimentCommands.DecideControlledExperimentAsync(
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
            return ResourceNotFound("影子建议不存在。");
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
            return ResourceNotFound("工艺操作域不存在。");
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
            return ResourceNotFound("工艺操作域不存在。");
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
            return ResourceNotFound("工艺操作域不存在。");
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
            return ResourceNotFound("知识声明不存在。");
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
                    return AuthorizationDenied();
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
            return ResourceNotFound("迁移评估不存在。");
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
            return ResourceNotFound("研发项目不存在。");
        if (!CanAccess(project, identity.Identity!, requireWrite))
            return AuthorizationDenied();
        return await ExecuteRuleAsync(() => operation(identity.Identity!)).ConfigureAwait(false);
    }

    private Task<IActionResult> ExecuteResearchPageAsync<T>(
        Guid projectId,
        string? cursor,
        int limit,
        Func<string?, Task<ResearchPage<T>>> query,
        CancellationToken ct)
    {
        cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();
        if (limit is < 1 or > 200)
            return Task.FromResult<IActionResult>(
                InvalidRequest("Limit 必须在 1 到 200 之间。"));
        if (!string.IsNullOrWhiteSpace(cursor) &&
            !ResearchPageCursor.TryDecode(cursor, out _, out _))
            return Task.FromResult<IActionResult>(
                InvalidRequest("分页游标无效或已经损坏。"));
        return ExecuteForProjectAsync(
            projectId,
            false,
            async _ => Ok(await query(cursor).ConfigureAwait(false)),
            ct);
    }

    private (PlatformIdentity? Identity, IActionResult? Result) ResolveResearchIdentity()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return (null, AuthenticationRequired("需要平台统一认证。"));
        if (!identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator))
            return (null, AuthorizationDenied());
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

    private async Task<IActionResult> ExecuteRuleAsync(Func<Task<IActionResult>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (ResearchExperimentValidationException exception)
        {
            return StateConflict(exception.Message, ("errors", exception.Errors));
        }
        catch (ProcessResearchRuleException exception)
        {
            return StateConflict(exception.Message);
        }
        catch (ProcessOptimizerUnavailableException exception)
        {
            return ServiceUnavailable(exception.Message);
        }
    }
}

public sealed record ResearchStatusChangeRequest(string TargetStatus);

public sealed record ResearchProjectMembersUpdateRequest(
    int Revision,
    IReadOnlyList<string> MemberUserIds);
