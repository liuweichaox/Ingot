using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class CycleDiagnosisEngineTests
{
    [Fact]
    public void Analyze_RanksActualRecipeAndProcessFeatureCandidates()
    {
        var rows = new[]
        {
            Cycle("PASS-1", "PASS", 500, 1.0, "PRESS-A"),
            Cycle("PASS-2", "PASS", 501, 1.1, "PRESS-A"),
            Cycle("PASS-3", "PASS", 502, 1.2, "PRESS-A"),
            Cycle("FAIL-1", "FAIL", 515, 3.5, "PRESS-A"),
            Cycle("FAIL-2", "FAIL", 516, 3.6, "PRESS-A"),
            Cycle("FAIL-3", "FAIL", 517, 3.7, "PRESS-A")
        };

        var result = new CycleDiagnosisEngine().Analyze(rows);

        Assert.Equal(CycleDiagnosisEngine.AlgorithmVersion, result.AlgorithmVersion);
        Assert.Equal("exploratory", result.EvidenceLevel);
        Assert.Equal(3, result.PassCycleCount);
        Assert.Equal(3, result.FailCycleCount);
        var recipe = Assert.Single(result.Candidates, candidate =>
            candidate.SourceKind == CycleCauseSourceKinds.RecipeParameter);
        Assert.Equal("recipe:holding-temperature", recipe.DataSource);
        Assert.Equal(CycleCauseActionability.Controllable, recipe.Actionability);
        Assert.Equal(501d, recipe.PassMedian);
        Assert.Equal(516d, recipe.FailMedian);
        Assert.True(recipe.CandidateScore > 0);
        var feature = Assert.Single(result.Candidates, candidate =>
            candidate.SourceKind == CycleCauseSourceKinds.ProcessFeature);
        Assert.Equal("signal:mold-temperature:overshoot:press", feature.DataSource);
        Assert.Equal(CycleCauseActionability.Observable, feature.Actionability);
    }

    [Fact]
    public void Analyze_ExposesContextDifferencesInsteadOfClaimingCausation()
    {
        var rows = new[]
        {
            Cycle("PASS-1", "PASS", 500, 1.0, "PRESS-A"),
            Cycle("PASS-2", "PASS", 501, 1.1, "PRESS-A"),
            Cycle("FAIL-1", "FAIL", 515, 3.5, "PRESS-B"),
            Cycle("FAIL-2", "FAIL", 516, 3.6, "PRESS-B")
        };

        var result = new CycleDiagnosisEngine().Analyze(rows);

        Assert.All(result.Candidates, candidate =>
            Assert.Contains("machine_id", candidate.PossibleConfounders));
        Assert.Contains(result.Limitations, limitation =>
            limitation.Contains("混杂", StringComparison.Ordinal));
    }

    private static CycleComparisonRow Cycle(
        string id,
        string outcome,
        double holdingTemperature,
        double overshoot,
        string machineId)
        => new()
        {
            CorrelationId = id,
            MachineId = machineId,
            StartedAt = DateTimeOffset.Parse("2026-07-20T08:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-07-20T08:10:00Z"),
            ProductSeries = "lens-a",
            EvidenceWeight = 1,
            InspectionOutcomes = [outcome],
            RecipeId = "lens-molding",
            RecipeVersion = "1",
            RecipeParameters =
            [
                new CycleRecipeParameter
                {
                    Code = "holding-temperature",
                    Name = "保压温度",
                    Unit = "Cel",
                    Value = JsonSerializer.SerializeToElement(holdingTemperature)
                }
            ],
            Signals =
            [
                new CycleSignalStatistic
                {
                    Code = "mold-temperature",
                    Name = "模具温度",
                    Unit = "Cel",
                    Features =
                    [
                        new CycleSignalFeature
                        {
                            Code = "overshoot",
                            PhaseCode = "press",
                            PhaseName = "压制",
                            PhaseOrder = 1,
                            Value = overshoot
                        }
                    ]
                }
            ]
        };
}
