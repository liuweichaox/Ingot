// 覆盖实验审批、安全边界和机理知识约束。
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

public sealed class ProcessResearchWorkflowExperimentTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task Experiment_CreatorCannotApproveOwnPlan()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "单因素探索",
                Factors =
                [
                    new ResearchVariableSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = 520,
                        Unit = "Cel"
                    }
                ],
                RunPlan =
                [
                    Run("low", 1, 500, 10),
                    Run("high", 2, 530, 10)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复基线工艺规范。"
            },
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId,
                ResearchExperimentStatuses.Approved,
                "engineer-a"));

        Assert.Contains("创建人和批准人必须分离", error.Message);
    }

    [Fact]
    public async Task Experiment_RejectsRunThatViolatesDeclaredSafetyConstraint()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ExperimentCommands.CreateExperimentAsync(
                project.ProjectId,
                new ResearchExperiment
                {
                    Name = "越界实验",
                    RunPlan =
                    [
                        Run("safe", 1, 530, 10),
                        Run("unsafe", 2, 549, 10)
                    ],
                    ObjectiveCodes = ["form-error"],
                    StopRule = "触发安全约束时停止。",
                    RollbackPlan = "恢复安全基线。"
                },
                "engineer-a"));

        Assert.Contains("temperature-safety", error.Message);
    }

    [Fact]
    public async Task Project_NormalizesConstraintUnitToItsControlVariable()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var baseDraft = ProjectDraft();
        var draft = baseDraft with
        {
            Code = "constraint-unit-normalization",
            Constraints =
            [
                baseDraft.Constraints.Single() with { Limit = 818.15, Unit = "K" }
            ]
        };

        var project = await workflow.CreateProjectAsync(draft, "engineer-a");

        var constraint = Assert.Single(project.Constraints);
        Assert.Equal("Cel", constraint.Unit);
        Assert.Equal(545, constraint.Limit, 6);
    }

    [Fact]
    public async Task Experiment_CannotCompleteWithoutCalculatedResult()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "结果门禁验证",
                RunPlan =
                [
                    Run("low", 1, 500, 10),
                    Run("high", 2, 530, 10)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复基线工艺规范。"
            },
            "engineer-a");
        await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId,
                ResearchExperimentStatuses.Completed,
                "engineer-a"));

        Assert.Contains("必须记录由源数据计算得到的结果", error.Message);
    }

    [Fact]
    public void OptimizerRejectsConditionsBelowObservedProcessResolution()
    {
        var controls = new Dictionary<string, ResearchVariable>(StringComparer.Ordinal)
        {
            ["vacuum-position"] = new()
            {
                Code = "vacuum-position",
                Name = "真空位置",
                Role = ResearchVariableRoles.Control,
                Unit = "mm",
                LowerLimit = 24.25,
                UpperLimit = 25.352
            }
        };
        var observations = new[] { 24.25, 25.25, 25.352 }
            .Select(value => new OptimizerObservationInput
            {
                Params = new Dictionary<string, double> { ["vacuum-position"] = value }
            })
            .ToArray();
        var suggestions = new[] { 25.35040, 25.35071 }
            .Select(value => new OptimizerSuggestionOutput
            {
                RecommendedParameters = new Dictionary<string, double>
                {
                    ["vacuum-position"] = value
                }
            })
            .ToArray();

        var error = Assert.Throws<ProcessResearchRuleException>(() =>
            ResearchOptimizationService.EnsureExperimentConditionsAreDistinguishable(
                suggestions,
                observations,
                controls));

        Assert.Contains("不能伪装成两个实验条件", error.Message);
    }

    [Fact]
    public void MechanismKnowledge_NarrowsHardBoundsAndRanksOnlyApplicableActiveClaims()
    {
        var projectId = Guid.CreateVersion7();
        var project = ProjectDraft() with
        {
            ProjectId = projectId,
            Context = new Dictionary<string, string> { ["material-grade"] = "A" }
        };
        var claim = new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Status = MechanismClaimStatuses.Active,
            Name = "安全工艺窗",
            MechanismType = "constraint",
            Statement = "材料 A 仅在收窄工艺窗内稳定。",
            FalsificationCondition = "重复实验显示窗外仍稳定。",
            Applicability =
            [
                new MechanismClaimApplicability
                    { DimensionCode = "material-grade", DimensionValue = "A" }
            ],
            Constraints =
            [
                new MechanismClaimConstraint
                {
                    VariableCode = "holding-temperature", ConstraintKind = "safe-range",
                    Minimum = 500, Maximum = 530, Unit = "Cel", Severity = "hard"
                },
                new MechanismClaimConstraint
                {
                    VariableCode = "press-force", ConstraintKind = "preferred-range",
                    Minimum = 10, Maximum = 12, Unit = "kN", Severity = "soft"
                }
            ],
            CreatedBy = "engineer-a",
            ContentHash = new string('a', 64)
        };

        var selected = MechanismKnowledgeExperimentPolicy.Select(project, [claim], []);
        var campaign = MechanismKnowledgeExperimentPolicy.ApplyHardConstraints(
            ResearchOptimizationService.BuildCampaign(
                project, ResearchOptimizationIntents.ReachSpecification, null),
            selected);
        Assert.Contains(campaign.Constraints, value =>
            value.Variable == "holding-temperature" && value.Operator == ">=" && value.Limit == 500);
        Assert.Contains(campaign.Constraints, value =>
            value.Variable == "holding-temperature" && value.Operator == "<=" && value.Limit == 530);

        var ranked = MechanismKnowledgeExperimentPolicy.Rank(
            [Suggestion(520, 18, 0.9), Suggestion(520, 11, 0.1)],
            selected,
            project.Variables.Where(value => value.Role == ResearchVariableRoles.Control)
                .ToDictionary(value => value.Code, StringComparer.Ordinal));
        Assert.Equal(18, ranked[0].RecommendedParameters["press-force"]);

        var error = Assert.Throws<ProcessResearchRuleException>(() =>
            MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(
                Suggestion(540, 11, 0.5), selected));
        Assert.Contains("holding-temperature", error.Message);
    }

    [Fact]
    public async Task MechanismKnowledgeSnapshotChange_BlocksExistingExperiment()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var emptySnapshot = MechanismKnowledgeExperimentPolicy.SnapshotHash(
            new AppliedMechanismKnowledge([], [], [], []));
        var claim = new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Version = 1,
            Status = MechanismClaimStatuses.Active,
            Name = "新激活的保压窗口",
            MechanismType = "constraint",
            Statement = "该知识在实验计划生成后才激活。",
            FalsificationCondition = "独立实验不支持该窗口。",
            Applicability =
            [
                new MechanismClaimApplicability
                    { DimensionCode = "project-code", DimensionValue = project.Code }
            ],
            ContentHash = new string('a', 64)
        };
        var knowledgeStore = new ReplayMechanismKnowledgeStore(claim);
        var gate = new ResearchExperimentKnowledgeGate(store, knowledgeStore);
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Name = "基于旧知识快照的实验",
            Status = ResearchExperimentStatuses.Planned,
            StopRule = "触发安全约束时停止。",
            RollbackPlan = "恢复已发布工艺规范。",
            RunPlan = [Run("snapshot-change", 1, 515, 10)],
            Optimization = new ResearchOptimizationMetadata
            {
                ModelVersion = "snapshot-test",
                InputHash = new string('b', 64),
                MechanismKnowledgeSnapshotHash = emptySnapshot,
                MechanismModelSnapshotHash = "none"
            }
        };

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => gate.ValidateAsync(experiment));

        Assert.Contains("机理知识已发生变化", error.Message);

        var currentKnowledge = MechanismKnowledgeExperimentPolicy.Select(project, [claim], []);
        await gate.ValidateAsync(experiment with
        {
            Optimization = experiment.Optimization with
            {
                MechanismKnowledgeSnapshotHash =
                    MechanismKnowledgeExperimentPolicy.SnapshotHash(currentKnowledge)
            }
        });
    }

}
