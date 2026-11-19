// 覆盖执行一致性评估的容差、缺失参数和失败关闭边界。
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessResearch;
using System.Reflection;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchExecutionAdherenceEvaluatorTests
{
    [Theory]
    [InlineData(520.49, ResearchExecutionAdherenceStatuses.WithinTolerance)]
    [InlineData(520.5, ResearchExecutionAdherenceStatuses.OutsideTolerance)]
    [InlineData(521, ResearchExecutionAdherenceStatuses.OutsideTolerance)]
    public async Task Evaluate_DoesNotTreatAdjacentStepsAsTheSelectedSetting(double actual, string status)
    {
        var store = DispatchProxy.Create<IProcessConfigurationStore, StepConfigurationProxy>();
        var brief = Brief() with
        {
            Context = new Dictionary<string, string> { ["process_specification_version"] = "1" }
        };
        var evaluator = new ResearchExecutionAdherenceEvaluator(new RecipeRecommendationBriefPolicy(store));

        var result = await evaluator.EvaluateAsync(brief, Decision(520, 12), Observation(actual, 12));

        Assert.Equal(status, result.Status);
        Assert.Equal("execution-adherence-v2", result.PolicyVersion);
        Assert.Equal(0.5, result.Variables.Single(value => value.VariableCode == "holding-temperature").Tolerance);
    }

    [Fact]
    public async Task Evaluate_MarksUnverifiableWhenPinnedRecipeCannotBeRead()
    {
        var store = DispatchProxy.Create<IProcessConfigurationStore, StepConfigurationProxy>();
        ((StepConfigurationProxy)store).RecipeMissing = true;
        var brief = Brief() with
        {
            Context = new Dictionary<string, string> { ["process_specification_version"] = "1" }
        };
        var evaluator = new ResearchExecutionAdherenceEvaluator(new RecipeRecommendationBriefPolicy(store));

        var result = await evaluator.EvaluateAsync(brief, Decision(520, 12), Observation(521, 12));

        Assert.Equal(ResearchExecutionAdherenceStatuses.Unverifiable, result.Status);
        Assert.Contains("无法读取", result.Reason);
    }

    public class StepConfigurationProxy : DispatchProxy
    {
        public bool RecipeMissing { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                nameof(IProcessConfigurationStore.ListProcessSpecificationsAsync) when RecipeMissing =>
                    Task.FromResult<IReadOnlyList<ProcessSpecification>>([]),
                nameof(IProcessConfigurationStore.ListProcessSpecificationsAsync) =>
                    Task.FromResult<IReadOnlyList<ProcessSpecification>>([
                        new ProcessSpecification
                        {
                            ProcessSpecificationId = "recipe-001", Name = "Recipe", Version = 1,
                            DataModelId = "model", Status = ConfigurationStatuses.Published
                        }
                    ]),
                nameof(IProcessConfigurationStore.GetDataModelAsync) =>
                    Task.FromResult<ProcessDataModel?>(new ProcessDataModel
                    {
                        ModelId = "model", Name = "Model",
                        ControlParameters = [new ControlParameterDefinition
                        {
                            Code = "holding-temperature", DisplayName = "Temperature", Step = 1
                        }]
                    }),
                _ => throw new NotSupportedException(targetMethod?.Name)
            };
    }

    [Fact]
    public async Task Evaluate_MarksActualParametersWithinTolerance()
    {
        var result = await new ResearchExecutionAdherenceEvaluator().EvaluateAsync(
            Brief(), Decision(520, 12), Observation(525, 12.05));

        Assert.Equal(ResearchExecutionAdherenceStatuses.WithinTolerance, result.Status);
        Assert.All(result.Variables, variable => Assert.True(variable.WithinTolerance));
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task Evaluate_MarksActualParametersOutsideTolerance()
    {
        var result = await new ResearchExecutionAdherenceEvaluator().EvaluateAsync(
            Brief(), Decision(520, 12), Observation(540, 13));

        Assert.Equal(ResearchExecutionAdherenceStatuses.OutsideTolerance, result.Status);
        Assert.Contains(result.Variables, variable => !variable.WithinTolerance);
        Assert.Contains("超出容差", result.Reason ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evaluate_MarksUnverifiableWhenActualParameterIsMissing()
    {
        var result = await new ResearchExecutionAdherenceEvaluator().EvaluateAsync(
            Brief(), Decision(520, 12), Observation(541));

        Assert.Equal(ResearchExecutionAdherenceStatuses.Unverifiable, result.Status);
        Assert.Contains("无法核对", result.Reason ?? "", StringComparison.Ordinal);
    }

    private static RecipeRecommendationBrief Brief()
        => new()
        {
            SiteCode = "SITE-001",
            ProcessSpecificationId = "recipe-001",
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "form-error", Name = "面形误差", Unit = "um", Direction = "minimize", Target = 0.4
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "holding-temperature", Name = "保压温度", Role = ResearchVariableRoles.Control,
                    Unit = "Cel", LowerLimit = 480, UpperLimit = 550
                },
                new ResearchVariable
                {
                    Code = "press-force", Name = "模压力", Role = ResearchVariableRoles.Control,
                    Unit = "kN", LowerLimit = 5, UpperLimit = 20
                }
            ]
        };

    private static ResearchRecipeRecommendationDecision Decision(double temperature, double force)
        => new()
        {
            RecommendationId = Guid.CreateVersion7(),
            RecommendationKey = "recipe-001",
            SiteCode = "SITE-001",
            ProcessSpecificationId = "recipe-001",
            BriefHash = new string('a', 64),
            Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
            SuggestedParameters = Parameters(temperature, force),
            EngineerSelectedParameters = Parameters(temperature, force),
            Prediction = new OptimizationRunPrediction
            {
                ExecutionKey = "recipe-001",
                Rationale = "test"
            },
            DecisionSnapshotHash = new string('b', 64)
        };

    private static ResearchRunObservation Observation(params double[] actualValues)
        => new()
        {
            ExecutionKey = "run-001",
            ActualFactors = actualValues.Length > 1
                ? [Setting("holding-temperature", actualValues[0], "Cel"), Setting("press-force", actualValues[1], "kN")]
                : [Setting("holding-temperature", actualValues[0], "Cel")],
            SourceContentHash = new string('c', 64)
        };

    private static IReadOnlyList<ResearchVariableSetting> Parameters(double temperature, double force)
        => [Setting("holding-temperature", temperature, "Cel"), Setting("press-force", force, "kN")];

    private static ResearchVariableSetting Setting(string code, double value, string unit)
        => new() { VariableCode = code, Value = value, Unit = unit };
}
