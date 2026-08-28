// 覆盖优化、配方建议和特征验证。
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

public sealed class ProcessResearchWorkflowOptimizationTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task Optimizer_CreatesAnOrdinaryExperimentFromPerRunObservations()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var methodAdmission = await SeedMethodAdmissionEvidenceAsync(store, project);
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = "fx3u-execution-snapshot",
            RunObservations =
            [
                new ResearchRunObservation
                {
                    ExecutionKey = "fx3u-execution-1",
                    ActualFactors =
                    [
                        new ResearchVariableSetting
                        {
                            VariableCode = "holding-temperature", Value = 510, Unit = "Cel"
                        },
                        new ResearchVariableSetting
                        {
                            VariableCode = "press-force", Value = 10, Unit = "kN"
                        }
                    ],
                    Outcomes = new Dictionary<string, double> { ["form-error"] = 0.8 },
                    SourceContentHash = new string('c', 64)
                },
                new ResearchRunObservation
                {
                    ExecutionKey = "fx3u-execution-context-missing",
                    ActualFactors =
                    [
                        new ResearchVariableSetting
                        {
                            VariableCode = "holding-temperature", Value = 520, Unit = "Cel"
                        },
                        new ResearchVariableSetting
                        {
                            VariableCode = "press-force", Value = 12, Unit = "kN"
                        }
                    ],
                    Outcomes = new Dictionary<string, double> { ["form-error"] = 0.7 },
                    ValidForOptimization = false,
                    ExclusionReason = "缺少分析必需上下文：tooling_assembly_id",
                    SourceContentHash = new string('d', 64)
                }
            ]
        });
        var optimizer = new ResearchOptimizationService(
            store,
            new StubOptimizerClient(),
            new EmptyObservationAssembler(),
            new ResearchExperimentResultMaterializer(workflow),
            workflow.ExperimentCommands,
            workflow);
        var methodAssessment = await optimizer.AssessMethodAdmissionAsync(project.ProjectId);

        var experiment = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest
            {
                BatchSize = 2,
                ReplicatesPerCondition = 2,
                Seed = 7
            },
            "engineer-a");
        var repeated = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest
            {
                BatchSize = 2,
                ReplicatesPerCondition = 2,
                Seed = 7
            },
            "engineer-a");

        Assert.Equal(ResearchDesignMethods.BayesianOptimization, experiment.DesignMethod);
        Assert.True(methodAssessment.Eligible);
        Assert.Equal(methodAdmission.ReportId, methodAssessment.HistoricalReplayReportId);
        Assert.Equal(experiment.ExperimentId, repeated.ExperimentId);
        Assert.Equal(ResearchExperimentStatuses.Planned, experiment.Status);
        Assert.Equal(4, experiment.RunPlan.Count);
        Assert.Equal(2, experiment.RunPlan.Select(static value => value.BlockKey).Distinct().Count());
        Assert.All(
            experiment.RunPlan.GroupBy(static value => value.ReplicateKey),
            static group => Assert.Equal(2, group.Count()));
        Assert.Equal(4, experiment.Execution?.Commands.Count);
        Assert.NotNull(experiment.Optimization);
        Assert.Equal(1, experiment.Optimization.ObservationCount);
        Assert.Equal("botorch-qlogbo-test", experiment.Optimization.ModelVersion);
        Assert.Equal("declared-test-features", experiment.Optimization.FeatureSetId);
        Assert.Equal(1, experiment.Optimization.DerivedFeatureCount);
        Assert.Equal(methodAdmission.ReportId,
            experiment.Optimization.MethodAdmission!.HistoricalReplayReportId);
        Assert.Equal(methodAdmission.ReportHash,
            experiment.Optimization.MethodAdmission.HistoricalReplayReportHash);

        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        var assembly = new ResearchObservationAssembly(
            experiment.RunPlan.Select((run, index) => new ResearchRunObservation
            {
                ExecutionKey = run.ExecutionKey,
                ActualFactors = run.Factors,
                Outcomes = new Dictionary<string, double>
                {
                    ["form-error"] = index % 2 == 0 ? 0.22 : 0.24
                },
                SourceContentHash = new string("defa"[index], 64)
            }).ToArray(),
            experiment.RunPlan.Count);
        var operatingRegionMaterializer = new ResearchOperatingRegionMaterializer(store, workflow);
        var resultMaterializer = new ResearchExperimentResultMaterializer(
            workflow,
            operatingRegionMaterializer,
            store);
        var results = await resultMaterializer.MaterializeCompletedAsync(
            project,
            [experiment],
            await store.ListExperimentResultsAsync(project.ProjectId),
            assembly,
            "system-result-materialization");
        var result = Assert.Single(results);
        Assert.Equal(2, result.ReplicateCount);
        Assert.Equal(2, result.DistinctBlockCount);
        var candidate = Assert.Single(await store.ListOperatingRegionsAsync(project.ProjectId));
        Assert.Equal(OperatingRegionStatuses.Candidate, candidate.Status);
        Assert.Equal(OperatingRegionValidationLevels.Evidence, candidate.ValidationLevel);
        Assert.All(candidate.Variables, static variable =>
            Assert.Equal(variable.LowerBound, variable.UpperBound));

        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Validating,
            "engineer-a");
        await CompleteIndependentValidationAsync(workflow, candidate);
        var validated = await workflow.ValidateOperatingRegionAsync(
            candidate.OperatingRegionId,
            "engineer-b");
        Assert.Equal(OperatingRegionValidationLevels.Laboratory, validated.ValidationLevel);
    }

    [Fact]
    public async Task Optimizer_PausesReachSpecificationWhenCurrentReplayFailsSimpleBaselineGate()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "optimizer-method-gate"
        }, "engineer-a");
        await SeedMethodAdmissionEvidenceAsync(store, project);
        await SeedMethodAdmissionEvidenceAsync(
            store,
            project,
            gatePassed: false,
            gateFailure: "优化器达到规格的中位试验数劣于预注册的二次响应面基线。");
        var optimizer = new ResearchOptimizationService(
            store,
            new StubOptimizerClient(),
            new EmptyObservationAssembler(),
            new ResearchExperimentResultMaterializer(workflow),
            workflow.ExperimentCommands,
            workflow);
        var assessment = await optimizer.AssessMethodAdmissionAsync(project.ProjectId);

        var exception = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            optimizer.CreateNextExperimentAsync(
                project.ProjectId,
                new ResearchOptimizationRequest(),
                "engineer-a"));

        Assert.False(assessment.Eligible);
        Assert.Contains(assessment.Failures, value =>
            value.Contains("二次响应面", StringComparison.Ordinal));
        Assert.Contains("正则化响应面", assessment.FallbackMethods);
        Assert.Contains("序贯优化已暂停", exception.Message, StringComparison.Ordinal);
        Assert.Contains("二次响应面", exception.Message, StringComparison.Ordinal);
        Assert.Contains("适用 DOE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipeRecommendation_UsesIndependentStorageAndNaturalRuns()
    {
        var store = new MemoryStore();
        var validation = new ResearchExperimentValidationService(store);
        var workflow = CreateWorkflow(store, experimentValidation: validation);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "natural-recipe-recommendation"
        }, "engineer-a");
        var observations = new MutableNaturalObservationAssembler(
        [
            NaturalObservation("production-1", 500, 8, 0.62),
            NaturalObservation("production-2", 510, 10, 0.48),
            NaturalObservation("production-3", 520, 12, 0.39)
        ]);
        var optimizer = new ResearchOptimizationService(
            store,
            new RecipeOptimizerClient(),
            observations,
            new ResearchExperimentResultMaterializer(workflow),
            workflow.ExperimentCommands,
            workflow);

        var first = await optimizer.CreateNextRecipeRecommendationAsync(
            project.ProjectId,
            new ResearchRecipeRecommendationRequest { Seed = 11 },
            "engineer-a");
        var repeated = await optimizer.CreateNextRecipeRecommendationAsync(
            project.ProjectId,
            new ResearchRecipeRecommendationRequest { Seed = 11 },
            "engineer-a");

        Assert.Equal(first.RecommendationId, repeated.RecommendationId);
        Assert.Equal(3, first.ObservationCount);
        Assert.Single(first.Items);
        Assert.True(first.RequiresEngineerConfirmation);
        Assert.False(ResearchOptimizationModes.IsValid("recipe-recommendation"));
        var stored = Assert.Single(await store.ListRecipeRecommendationsAsync(project.ProjectId));
        Assert.Equal(first, stored);
        Assert.Empty(await store.ListExperimentsAsync(project.ProjectId));
        Assert.Null(await store.GetExperimentAsync(first.RecommendationId));
        Assert.Contains(await store.ListAuditEntriesAsync(project.ProjectId), entry =>
            entry.ResourceType == "recipe-recommendation" &&
            entry.ResourceId == first.RecommendationId.ToString());

        observations.Values =
        [
            .. observations.Values,
            NaturalObservation("production-4", 525, 13, 0.35)
        ];
        var afterNewProductionRun = await optimizer.CreateNextRecipeRecommendationAsync(
            project.ProjectId,
            new ResearchRecipeRecommendationRequest { Seed = 11 },
            "engineer-a");

        Assert.NotEqual(first.RecommendationId, afterNewProductionRun.RecommendationId);
        Assert.Equal(4, afterNewProductionRun.ObservationCount);
        Assert.Equal(2, (await store.ListRecipeRecommendationsAsync(project.ProjectId)).Count);
        Assert.Empty(await store.ListExperimentsAsync(project.ProjectId));
    }

    [Fact]
    public async Task Project_RejectsHiddenOrInvalidDerivedFeatureLogic()
    {
        var workflow = CreateWorkflow(new MemoryStore());
        var invalid = ProjectDraft() with
        {
            Code = "invalid-derived-feature",
            OptimizationFeatures = new ResearchOptimizationFeatureSet
            {
                FeatureSetId = "invalid-feature-set",
                DerivedFeatures =
                [
                    new ResearchDerivedFeature
                    {
                        Name = "hidden-domain-rule",
                        Operator = ResearchDerivedFeatureOperators.Identity,
                        Inputs = ["variable-guessed-from-name"]
                    }
                ]
            }
        };

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.CreateProjectAsync(invalid, "engineer-a"));

        Assert.Contains("未知或尚未定义", error.Message, StringComparison.Ordinal);
    }

}
