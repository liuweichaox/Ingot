using System.Reflection;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchObservationCohortTests
{
    [Fact]
    public void ExcludedCrossCellsCannotMakeConfoundedEligibleRunsPass()
    {
        var observations = new[]
        {
            Observation("a1", "a", 10), Observation("a2", "a", 10),
            Observation("b1", "b", 20), Observation("b2", "b", 20),
            Observation("excluded-a", "a", 20, false), Observation("excluded-b", "b", 10, false)
        };

        var result = Apply(observations);

        Assert.All(result, value => Assert.False(value.ValidForOptimization));
        Assert.Contains("重叠", result[0].ExclusionReason);
        Assert.Equal("缺少质量结果", result[4].ExclusionReason);
    }

    [Fact]
    public void EligibleCrossCellsPassEvenWhenExcludedRunsLackContext()
    {
        var observations = new[]
        {
            Observation("a1", "a", 10), Observation("a2", "a", 20),
            Observation("b1", "b", 10), Observation("b2", "b", 20),
            Observation("excluded", "", 10, false)
        };

        var result = Apply(observations);

        Assert.All(result.Take(4), value => Assert.True(value.ValidForOptimization));
        Assert.False(result[4].ValidForOptimization);
        Assert.Equal("缺少质量结果", result[4].ExclusionReason);
    }

    private static ResearchRunObservation Observation(string id, string lot, double factor, bool valid = true) => new()
    {
        ExecutionKey = id,
        SourceContentHash = id,
        Context = new Dictionary<string, string> { ["lot"] = lot },
        ActualFactors = [new ResearchVariableSetting { VariableCode = "temperature", Value = factor, Unit = "C" }],
        ValidForOptimization = valid,
        ExclusionReason = valid ? null : "缺少质量结果"
    };

    private static IReadOnlyList<ResearchRunObservation> Apply(ResearchRunObservation[] observations)
    {
        var package = new ScenarioPackage
        {
            PackageId = "test", Name = "test", DataModelId = "test", AnalysisPlanId = "test",
            ContextFields = [new ScenarioContextFieldPolicy
            {
                FieldCode = "lot", Name = "批次", MinimumCoverage = 1, MinimumFactorOverlap = 0.75
            }]
        };
        var method = typeof(ResearchObservationAssembler).GetMethod(
            "ApplyCohortContextGates", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IReadOnlyList<ResearchRunObservation>)method.Invoke(null, [observations, package])!;
    }
}
