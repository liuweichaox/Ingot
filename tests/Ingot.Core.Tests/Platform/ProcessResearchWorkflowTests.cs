// 覆盖执行证据生成、单位换算、假设结构和 API 暴露边界。
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

public sealed class ProcessResearchWorkflowEvidenceTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task ExecutionEvidenceService_RejectsInvalidComparisonBeforeReadingExecutions()
    {
        var store = new MemoryStore();
        var commands = new ResearchExperimentCommands(new ResearchExperimentCommandStoreAdapter(store));
        var comparisons = new RejectingExecutionComparisonService();
        var service = new ResearchExecutionEvidenceService(
            store,
            new ProcessResearchWorkflow(store, commands),
            commands,
            comparisons);

        await Assert.ThrowsAsync<ProcessResearchRuleException>(() => service.ProposeHypothesesAsync(
            Guid.CreateVersion7(),
            new ResearchHypothesisFromExecutionComparisonRequest
            {
                BaselineProcessExecutionId = "run-a",
                ProcessExecutionIds = ["run-a"],
                MaximumHypotheses = 3
            },
            "engineer-a",
            CancellationToken.None));

        Assert.Equal(0, comparisons.CallCount);
    }

    [Fact]
    public async Task ExecutionEvidenceService_RejectsExploratoryComparisonBeforeCreatingHypotheses()
    {
        var store = new MemoryStore();
        var commands = new ResearchExperimentCommands(new ResearchExperimentCommandStoreAdapter(store));
        var project = await new ProcessResearchWorkflow(store, commands)
            .CreateProjectAsync(ProjectDraft(), "engineer-a");
        var comparisons = new FixedExecutionComparisonService(
            Comparison("exploratory", -0.2, "exploratory"));
        var service = new ResearchExecutionEvidenceService(
            store,
            new ProcessResearchWorkflow(store, commands),
            commands,
            comparisons);

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.ProposeHypothesesAsync(
                project.ProjectId,
                new ResearchHypothesisFromExecutionComparisonRequest
                {
                    BaselineProcessExecutionId = "run-a",
                    ProcessExecutionIds = ["run-a", "run-b"],
                    MaximumHypotheses = 3
                },
                "engineer-a",
                CancellationToken.None));

        Assert.Contains("探索性证据", error.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ListHypothesesAsync(project.ProjectId));
    }

    [Fact]
    public async Task ExecutionEvidenceService_CreatesHypothesisOnlyFromStableRankedComparison()
    {
        var store = new MemoryStore();
        var commands = new ResearchExperimentCommands(new ResearchExperimentCommandStoreAdapter(store));
        var workflow = new ProcessResearchWorkflow(store, commands);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var service = new ResearchExecutionEvidenceService(
            store,
            workflow,
            commands,
            new FixedExecutionComparisonService(
                Comparison("candidate-ranking", 0.4, "stable")));

        var created = await service.ProposeHypothesesAsync(
            project.ProjectId,
            new ResearchHypothesisFromExecutionComparisonRequest
            {
                BaselineProcessExecutionId = "run-a",
                ProcessExecutionIds = ["run-a", "run-b"],
                MaximumHypotheses = 3
            },
            "engineer-a",
            CancellationToken.None);

        var hypothesis = Assert.Single(created);
        Assert.Contains("观察性关联", hypothesis.Rationale, StringComparison.Ordinal);
        Assert.Equal(["holding-temperature"], hypothesis.VariableCodes);
    }
    [Fact]
    public void UnitConverter_ConvertsKnownIndustrialAliases_AndRejectsUnknownDimensions()
    {
        Assert.True(ProcessUnitConverter.TryConvert(10, "bar", "MPa", out var mpa));
        Assert.Equal(1, mpa, 8);
        Assert.True(ProcessUnitConverter.TryConvert(1, "kgf/cm²", "bar", out var bar));
        Assert.Equal(0.980665, bar, 6);
        Assert.True(ProcessUnitConverter.TryConvert(25, "℃", "K", out var kelvin));
        Assert.Equal(298.15, kelvin, 8);
        Assert.True(ProcessUnitConverter.TryConvert(25, "Cel", "K", out var canonicalKelvin));
        Assert.Equal(298.15, canonicalKelvin, 8);
        Assert.False(ProcessUnitConverter.TryConvert(1, "HRC", "MPa", out _));
    }

    [Fact]
    public async Task Hypothesis_PreservesCausalTemporalInteractionFailureAndFalsificationStructure()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");

        var saved = await workflow.SaveHypothesisAsync(project.ProjectId, new ResearchHypothesis
        {
            Statement = "保压温度通过材料流动状态影响压力响应。",
            Rationale = "来源于过程曲线和工程机理。",
            VariableCodes = ["holding-temperature", "press-force"],
            CausalChain = [new ResearchHypothesisCausalLink { FromVariableCode = "holding-temperature", ToVariableCode = "press-force", Mechanism = "温度改变材料黏度和压力传递。", Direction = "decrease" }],
            TemporalFeatures = [new ResearchHypothesisTemporalFeature { VariableCode = "press-force", FeatureCode = "pressure-rise", PhaseCode = "holding", DelayMilliseconds = 500, WindowMilliseconds = 3000 }],
            Interactions = [new ResearchHypothesisInteraction { VariableCodes = ["holding-temperature", "press-force"], Description = "温度改变压力效应强度。" }],
            FailureConditions = [new ResearchHypothesisFailureCondition { Condition = "温度超过材料稳定边界", ObservableSignal = "颜色或挥发物异常", RequiredResponse = "停止实验并恢复基线" }],
            FalsificationConditions = ["升高温度后压力响应及质量结果均没有方向性变化。"],
            Confidence = 0.4
        }, "engineer-a");

        Assert.Single(saved.CausalChain);
        Assert.Equal(500, Assert.Single(saved.TemporalFeatures).DelayMilliseconds);
        Assert.Equal(2, Assert.Single(saved.Interactions).VariableCodes.Count);
        Assert.Single(saved.FailureConditions);
        Assert.Single(saved.FalsificationConditions);
    }

    [Fact]
    public void Api_ExposesOnlySourceMaterializationForExperimentResults()
    {
        var postRoutes = typeof(ResearchProjectsController)
            .GetMethods()
            .SelectMany(static method => method.CustomAttributes)
            .Where(static attribute => attribute.AttributeType.Name == "HttpPostAttribute")
            .SelectMany(static attribute => attribute.ConstructorArguments)
            .Select(static argument => argument.Value as string)
            .Where(static value => value is not null)
            .ToArray();

        Assert.Contains(
            "experiments/{experimentId:guid}/materialize-result",
            postRoutes);
        Assert.Contains(
            "experiments/{experimentId:guid}/runs/{suggestionExecutionKey}/shadow-decision",
            postRoutes);
        Assert.Contains(
            "shadow-recommendations/{recommendationId:guid}/materialize-outcome",
            postRoutes);
        Assert.DoesNotContain(
            "experiments/{experimentId:guid}/results",
            postRoutes);
    }
}
