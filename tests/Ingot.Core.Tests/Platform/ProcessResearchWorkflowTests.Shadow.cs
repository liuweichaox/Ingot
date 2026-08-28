// 覆盖影子建议、结果冻结和停止信号。
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessResearchWorkflowShadowTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task ShadowRecommendation_PreregistersDecision_ThenFreezesSourceOutcome()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var experiment = await CreateOptimizationExperimentAsync(workflow, project.ProjectId);
        var dispatchError = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId, ResearchExperimentStatuses.Approved, "engineer-b"));
        Assert.Contains("不能批准", dispatchError.Message, StringComparison.Ordinal);
        var assembler = new StubShadowObservationAssembler(new ResearchRunObservation
        {
            ExecutionKey = "production-execution-001",
            Context = new Dictionary<string, string>
            {
                ["equipment_id"] = "press-01",
                ["material_lot_ref"] = "lot-b"
            },
            ActualFactors =
            [
                new ResearchVariableSetting
                    { VariableCode = "holding-temperature", Value = 521, Unit = "Cel" },
                new ResearchVariableSetting
                    { VariableCode = "press-force", Value = 12.2, Unit = "kN" }
            ],
            ProcessFeatures = new Dictionary<string, double> { ["temperature.average"] = 520.4 },
            Outcomes = new Dictionary<string, double> { ["form-error"] = 0.31 },
            SourceContentHash = new string('a', 64)
        });
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = experiment.ExperimentId,
            DatasetSnapshotId = "historical-applicability",
            RunObservations =
            [
                new ResearchRunObservation
                {
                    ExecutionKey = "historical-1",
                    Context = new Dictionary<string, string> { ["equipment_id"] = "press-00" },
                    ActualFactors = experiment.RunPlan[0].Factors,
                    SourceContentHash = new string('1', 64)
                },
                new ResearchRunObservation
                {
                    ExecutionKey = "historical-2",
                    Context = new Dictionary<string, string> { ["equipment_id"] = "press-00" },
                    ActualFactors = experiment.RunPlan[1].Factors,
                    SourceContentHash = new string('2', 64)
                }
            ]
        });
        var service = new ResearchShadowRecommendationService(store, assembler);

        var recorded = await service.RecordDecisionAsync(
            experiment.ExperimentId,
            experiment.RunPlan[0].ExecutionKey,
            new ResearchShadowDecisionRequest
            {
                Decision = ResearchShadowDecisionStatuses.Modified,
                ActualExecutionKey = "production-execution-001",
                EngineerSelectedFactors =
                [
                    new ResearchVariableSetting
                        { VariableCode = "holding-temperature", Value = 520, Unit = "Cel" },
                    new ResearchVariableSetting
                        { VariableCode = "press-force", Value = 12, Unit = "kN" }
                ],
                RejectionReason = "当前材料批次要求降低升温幅度。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful,
                SiteLimitations = ["材料批次升温速率限制"],
                ContextSnapshot = new Dictionary<string, string>
                {
                    ["equipment_id"] = "press-01",
                    ["material_lot_ref"] = "lot-b"
                }
            },
            "engineer-b");

        Assert.Equal(64, recorded.DecisionSnapshotHash.Length);
        Assert.Equal("botorch-test", recorded.ModelVersion);
        Assert.Null(recorded.Outcome);
        Assert.Equal("optical-molding-window", recorded.ContextSnapshot["project_code"]);
        Assert.Equal(ResearchApplicabilityStatuses.ContextShift, recorded.Applicability.Status);
        Assert.Contains("equipment_id=press-01", recorded.Applicability.UnseenContextValues);
        var completed = await service.MaterializeOutcomeAsync(
            recorded.RecommendationId, "engineer-b");
        Assert.NotNull(completed.Outcome);
        Assert.Equal(6, completed.Outcome.SettingDeviationFromSuggestion["holding-temperature"]);
        Assert.Equal(1, completed.Outcome.SettingDeviationFromEngineerSelection["holding-temperature"]);
        Assert.Equal(0.31, completed.Outcome.Outcomes["form-error"]);
        Assert.Equal(new string('a', 64), completed.Outcome.SourceContentHash);
        Assert.Equal("production-execution-001", assembler.RequestedExecutionKey);
        var report = await service.BuildReportAsync(project.ProjectId);
        Assert.Equal(1, report.TotalRecommendations);
        Assert.Equal(1, report.ModifiedCount);
        Assert.Equal(1, report.PartlyUsefulCount);
        Assert.Equal(0, report.UnratedUsefulnessCount);
        Assert.Equal(1, report.CompletedOutcomeCount);
        Assert.Equal(1, report.ContextShiftCount);
        Assert.Equal(1, report.SettingDeviationCount);
        Assert.Equal(1, Assert.Single(report.Calibration).CoveredCount);
        Assert.Empty(report.SafetyEvents);
        Assert.False(report.StopRecommended);
        Assert.Equal(64, report.ReportHash.Length);

        var frozen = await service.MaterializeOutcomeAsync(
            recorded.RecommendationId, "engineer-c");
        Assert.Equal(completed.Outcome.CapturedAt, frozen.Outcome!.CapturedAt);
    }

    [Fact]
    public async Task ShadowRecommendation_RejectsDecisionWithoutContextOrConsistentFactors()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var experiment = await CreateOptimizationExperimentAsync(workflow, project.ProjectId);
        var service = new ResearchShadowRecommendationService(
            store, new StubShadowObservationAssembler(null));
        var request = new ResearchShadowDecisionRequest
        {
            Decision = ResearchShadowDecisionStatuses.Accepted,
            ActualExecutionKey = "production-execution-002",
            EngineerSelectedFactors = experiment.RunPlan[0].Factors,
            ContextSnapshot = new Dictionary<string, string>()
        };

        var noContext = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(
                experiment.ExperimentId, experiment.RunPlan[0].ExecutionKey,
                request, "engineer-b"));
        Assert.Contains("上下文", noContext.Message, StringComparison.Ordinal);

        var inconsistent = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(
                experiment.ExperimentId,
                experiment.RunPlan[0].ExecutionKey,
                request with
                {
                    EngineerSelectedFactors =
                    [
                        new ResearchVariableSetting
                            { VariableCode = "holding-temperature", Value = 520, Unit = "Cel" },
                        new ResearchVariableSetting
                            { VariableCode = "press-force", Value = 12, Unit = "kN" }
                    ],
                    ContextSnapshot = new Dictionary<string, string> { ["equipment_id"] = "press-01" }
                },
                "engineer-b"));
        Assert.Contains("一致", inconsistent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShadowReport_StopsOnMeasuredSafetyViolation()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var draft = ProjectDraft() with
        {
            Code = "shadow-safety-report",
            OutcomeConstraints =
            [
                new ResearchOutcomeConstraint
                {
                    Code = "form-error-safety",
                    Description = "面形误差安全守门",
                    OutcomeCode = "form-error",
                    Operator = "<=",
                    Limit = 0.4,
                    Unit = "um",
                    SafetyCritical = true
                }
            ]
        };
        var project = await workflow.CreateProjectAsync(draft, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var experiment = await CreateOptimizationExperimentAsync(workflow, project.ProjectId);
        var service = new ResearchShadowRecommendationService(
            store,
            new StubShadowObservationAssembler(new ResearchRunObservation
            {
                ExecutionKey = "unsafe-run",
                ActualFactors = experiment.RunPlan[0].Factors,
                Outcomes = new Dictionary<string, double> { ["form-error"] = 0.52 },
                ConstraintOutcomes = new Dictionary<string, double>
                { ["form-error-safety"] = 0.52 },
                SourceContentHash = new string('e', 64)
            }));
        var decision = await service.RecordDecisionAsync(
            experiment.ExperimentId,
            experiment.RunPlan[0].ExecutionKey,
            new ResearchShadowDecisionRequest
            {
                Decision = ResearchShadowDecisionStatuses.Accepted,
                ActualExecutionKey = "unsafe-run",
                EngineerSelectedFactors = experiment.RunPlan[0].Factors,
                ContextSnapshot = new Dictionary<string, string> { ["equipment_id"] = "press-01" }
            },
            "engineer-b");
        await service.MaterializeOutcomeAsync(decision.RecommendationId, "engineer-b");

        var report = await service.BuildReportAsync(project.ProjectId);

        Assert.True(report.StopRecommended);
        Assert.Single(report.SafetyEvents);
        Assert.Contains(report.StopSignals, value =>
            value.Code == "safety-boundary-violation" && value.Severity == "stop");
    }

}
