// 覆盖研究项目、成员、激活和完成工作流。
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

public sealed class ProcessResearchWorkflowProjectTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task ProjectActivation_RequiresCurrentIndependentlyReviewedStageZeroPreregistration()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");

        var missing = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ChangeProjectStatusAsync(
                project.ProjectId, ResearchProjectStatuses.Active, "engineer-a"));
        Assert.Contains("阶段 0", missing.Message, StringComparison.Ordinal);

        var service = new ResearchValidationPreregistrationService(
            store,
            new StubReliabilityBaselineService());
        var frozen = await service.FreezeAsync(
            project.ProjectId, ValidPreregistrationRequest(), "engineer-a");
        Assert.Equal(64, frozen.ContentHash.Length);
        Assert.Equal(45, frozen.Plan.EngineerWorkflowBaselines.Single().TotalMinutes);
        Assert.Equal(12, frozen.ReliabilityBaseline.AnalyzedRunCount);
        Assert.Equal(0.75, frozen.ReliabilityBaseline.Rates.Single().Rate);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.ReviewAsync(frozen.PreregistrationId, "engineer-a"));
        var reviewed = await service.ReviewAsync(frozen.PreregistrationId, "engineer-b");
        Assert.Equal(ResearchValidationPreregistrationStatuses.Reviewed, reviewed.Status);
        Assert.True((await service.AssessAsync(project.ProjectId)).Eligible);

        var replacement = await service.FreezeAsync(
            project.ProjectId,
            ValidPreregistrationRequest() with { DataScope = "收窄到同设备、同材料批次" },
            "engineer-a");
        var awaitingCurrentReview = await service.AssessAsync(project.ProjectId);
        Assert.False(awaitingCurrentReview.Eligible);
        Assert.Contains("v2", Assert.Single(awaitingCurrentReview.Failures), StringComparison.Ordinal);
        await service.ReviewAsync(replacement.PreregistrationId, "engineer-b");
        Assert.True((await service.AssessAsync(project.ProjectId)).Eligible);

        var active = await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        Assert.Equal(ResearchProjectStatuses.Active, active.Status);
        Assert.True((await service.AssessAsync(project.ProjectId)).Eligible);
    }

    [Fact]
    public async Task ActivatingProject_ShouldFreezePublishedScenarioContextPolicy()
    {
        var scenario = ResearchContextAdmissionEvaluatorTests.OpticalScenario();
        var store = new MemoryStore();
        var workflow = CreateWorkflow(
            store,
            processConfigurations: new ScenarioOnlyConfigurationStore(scenario));
        var draft = await workflow.CreateProjectAsync(
            ProjectDraft() with
            {
                Context = new Dictionary<string, string>
                {
                    [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                        $"{scenario.PackageId}:{scenario.Version}",
                    [ResearchContextAdmissionEvaluator.PolicyHashContextKey] = "client-supplied-value"
                }
            },
            "engineer-a");
        Assert.False(draft.Context.ContainsKey(ResearchContextAdmissionEvaluator.PolicyHashContextKey));
        await FreezeAndReviewStageZeroAsync(store, draft);

        var active = await workflow.ChangeProjectStatusAsync(
            draft.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");

        Assert.Equal(
            ResearchContextAdmissionEvaluator.ComputePolicyHash(scenario),
            active.Context[ResearchContextAdmissionEvaluator.PolicyHashContextKey]);
        var changedContext = new Dictionary<string, string>(active.Context, StringComparer.Ordinal)
        {
            [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] = "another-package:1"
        };
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.UpdateProjectAsync(
                active.ProjectId,
                active with { Context = changedContext },
                "engineer-a"));
    }

    [Fact]
    public async Task ActivatingProject_ShouldRejectMutableDraftScenarioPackage()
    {
        var scenario = ResearchContextAdmissionEvaluatorTests.OpticalScenario() with
        {
            Status = ConfigurationStatuses.Draft
        };
        var store = new MemoryStore();
        var workflow = CreateWorkflow(
            store,
            processConfigurations: new ScenarioOnlyConfigurationStore(scenario));
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with
            {
                Context = new Dictionary<string, string>
                {
                    [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                        $"{scenario.PackageId}:{scenario.Version}"
                }
            },
            "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ChangeProjectStatusAsync(
                project.ProjectId,
                ResearchProjectStatuses.Active,
                "engineer-a"));
        Assert.Contains("已发布", error.Message);
    }

    [Fact]
    public async Task GeneralProjectUpdate_ShouldPreserveServerManagedMembers()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { MemberUserIds = ["engineer-a", "engineer-b"] },
            "engineer-a");

        var updated = await workflow.UpdateProjectAsync(
            project.ProjectId,
            project with { Name = "renamed", MemberUserIds = ["attacker"] },
            "engineer-b");

        Assert.Equal(["engineer-a", "engineer-b"], updated.MemberUserIds);
        Assert.DoesNotContain("attacker", updated.MemberUserIds);
    }

    [Fact]
    public async Task GeneralProjectUpdate_ShouldNotMoveProjectAcrossSites()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { SiteCode = "site-a" },
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.UpdateProjectAsync(
                project.ProjectId,
                project with { SiteCode = "site-b" },
                "engineer-a"));

        Assert.Contains("站点不能更改", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_ShouldPreserveCanonicalSiteIdentifierCasing()
    {
        var workflow = CreateWorkflow(new MemoryStore());

        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");

        Assert.Equal("SITE-001", project.SiteCode);
    }

    [Fact]
    public async Task MemberManagement_ShouldRequireOwnerOrAdministratorAndRevision()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { MemberUserIds = ["engineer-a", "engineer-b"] },
            "engineer-a");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            workflow.UpdateProjectMembersAsync(
                project.ProjectId,
                project.Revision,
                ["engineer-b", "engineer-c"],
                "engineer-b",
                false));

        var saved = await workflow.UpdateProjectMembersAsync(
            project.ProjectId,
            project.Revision,
            ["engineer-c"],
            "engineer-a",
            false);
        Assert.Equal(["engineer-c", "engineer-a"], saved.MemberUserIds);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.UpdateProjectMembersAsync(
                project.ProjectId,
                project.Revision,
                ["engineer-d"],
                "admin",
                true));
    }

    [Fact]
    public async Task ResearchProject_CompletesOnlyAfterValidatedOperatingRegion()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");

        var hypothesis = await workflow.SaveHypothesisAsync(
            project.ProjectId,
            new ResearchHypothesis
            {
                Statement = "保压温度和压力共同影响面形误差。",
                Rationale = "历史过程执行、物理机理和专家经验均指向该交互关系。",
                VariableCodes = ["holding-temperature", "press-force"],
                ValidationOutcomeCode = "form-error",
                ExpectedEffectDirection = ResearchHypothesisEffectDirections.Decrease,
                MinimumEffect = 0.2,
                FalsificationConditions = ["重复受控实验未观察到预期方向和最小效应时推翻该假设。"],
                Confidence = 0.6
            },
            "engineer-a");

        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                HypothesisId = hypothesis.HypothesisId,
                Name = "保压温度与压力验证实验",
                DesignMethod = ResearchDesignMethods.FullFactorial,
                Factors =
                [
                    new ResearchVariableSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = 520,
                        Unit = "Cel"
                    },
                    new ResearchVariableSetting
                    {
                        VariableCode = "press-force",
                        Value = 12,
                        Unit = "kN"
                    }
                ],
                RunPlan =
                [
                    Run("low-low", 1, 510, 10),
                    Run("high-high", 2, 530, 14)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复已验证基线工艺规范。"
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        var result = await workflow.RecordMaterializedExperimentResultAsync(
            experiment.ExperimentId,
            new ResearchExperimentResult
            {
                DatasetSnapshotId = "snapshot-2026-07-25",
                Metrics =
                [
                    new ExperimentMetricResult
                    {
                        ObjectiveCode = "form-error",
                        BaselineValue = 0.8,
                        ObservedValue = 0.35,
                        EffectValue = -0.45,
                        LowerConfidenceBound = -0.55,
                        UpperConfidenceBound = -0.35,
                        Unit = "um",
                        BaselineSampleCount = 12,
                        ExperimentSampleCount = 12,
                        ComputationMethod = "bootstrap difference"
                    }
                ],
                RunObservations =
                [
                    new ResearchRunObservation
                    {
                        ExecutionKey = "low-low",
                        ActualFactors =
                        [
                            new ResearchVariableSetting
                            {
                                VariableCode = "holding-temperature",
                                Value = 510,
                                Unit = "Cel"
                            },
                            new ResearchVariableSetting
                            {
                                VariableCode = "press-force",
                                Value = 10,
                                Unit = "kN"
                            }
                        ],
                        ProcessFeatures = new Dictionary<string, double>
                        {
                            ["temperature-overshoot"] = 1.2,
                            ["force-impulse"] = 42.0
                        },
                        Outcomes = new Dictionary<string, double>
                        {
                            ["form-error"] = 0.43
                        },
                        SourceContentHash = new string('a', 64)
                    },
                    new ResearchRunObservation
                    {
                        ExecutionKey = "high-high",
                        ActualFactors =
                        [
                            new ResearchVariableSetting
                            {
                                VariableCode = "holding-temperature",
                                Value = 530,
                                Unit = "Cel"
                            },
                            new ResearchVariableSetting
                            {
                                VariableCode = "press-force",
                                Value = 14,
                                Unit = "kN"
                            }
                        ],
                        ProcessFeatures = new Dictionary<string, double>
                        {
                            ["temperature-overshoot"] = 0.8,
                            ["force-impulse"] = 48.0
                        },
                        Outcomes = new Dictionary<string, double>
                        {
                            ["form-error"] = 0.35
                        },
                        SourceContentHash = new string('b', 64)
                    }
                ],
                // 调用方传入的汇总计数不可信，工作流必须从计划和观察重新计算。
                RunCount = 99,
                ReplicateCount = 99,
                DistinctBlockCount = 99,
                DistinctMaterialLotCount = 99,
                DistinctEquipmentCount = 99,
                SafetyPassed = true,
                CalculatedFromSource = true
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Completed,
            "engineer-a");

        var window = await workflow.SaveOperatingRegionAsync(
            project.ProjectId,
            new ResearchOperatingRegion
            {
                Name = "稳定成形窗口",
                Variables =
                [
                    new OperatingRegionVariable
                    {
                        VariableCode = "holding-temperature",
                        LowerBound = 520,
                        UpperBound = 520,
                        Unit = "Cel"
                    },
                    new OperatingRegionVariable
                    {
                        VariableCode = "press-force",
                        LowerBound = 12,
                        UpperBound = 12,
                        Unit = "kN"
                    }
                ],
                ObjectiveCodes = ["form-error"],
                SupportingExperimentIds = [experiment.ExperimentId],
                SupportingResultIds = [result.ResultId],
                Confidence = 0.9,
                ConfidenceMethod = ResearchConfidenceMethods.Bootstrap,
                AnalysisRunId = result.AnalysisRunId,
                AnalysisHash = result.AnalysisHash,
                Applicability = "材料批次 A，设备 PRESS-01。"
            },
            "engineer-a");

        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Validating,
            "engineer-a");
        var prematureValidation = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ValidateOperatingRegionAsync(window.OperatingRegionId, "engineer-b"));
        Assert.Contains("独立验证实验", prematureValidation.Message);
        await CompleteIndependentValidationAsync(workflow, window);
        window = await workflow.ValidateOperatingRegionAsync(window.OperatingRegionId, "engineer-b");
        Assert.Equal(OperatingRegionValidationLevels.Laboratory, window.ValidationLevel);
        var prematureRelease = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ReleaseOperatingRegionAsync(window.OperatingRegionId, "engineer-c"));
        Assert.Contains("受控在线运行", prematureRelease.Message);
        await SeedControlledOnlineValidationAsync(store, window);
        window = await workflow.ReleaseOperatingRegionAsync(window.OperatingRegionId, "engineer-c");
        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Completed,
            "engineer-a");

        Assert.Equal(OperatingRegionStatuses.Validated, window.Status);
        Assert.Equal(OperatingRegionValidationLevels.Production, window.ValidationLevel);
        Assert.Equal(ResearchProjectStatuses.Completed, project.Status);
        Assert.Equal(2, result.RunObservations.Count);
        Assert.Equal(2, result.RunCount);
        Assert.Equal(1, result.ReplicateCount);
        Assert.Equal(1, result.DistinctBlockCount);
        Assert.Equal(0, result.DistinctMaterialLotCount);
        Assert.Equal(0, result.DistinctEquipmentCount);
        var workspace = await workflow.GetWorkspaceAsync(project.ProjectId);
        Assert.Single(workspace.Hypotheses);
        Assert.Equal(ResearchHypothesisStatuses.Supported, workspace.Hypotheses[0].Status);
        Assert.Single(workspace.Hypotheses[0].ValidationEvidence);
        Assert.Equal(3, workspace.Experiments.Count);
        Assert.Single(workspace.OperatingRegions);
    }

}
