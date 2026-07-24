using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PostgresCycleScientificComputeEngineTests
{
    [Fact]
    public void Verify_AcceptsNumericallyEquivalentDatabaseFeature()
    {
        var feature = Feature(42, 900, 0.9, 10);
        var database = new PostgresCycleScientificComputeEngine.SqlScopeResult
        {
            Mean = 42 + 1e-11,
            ValidDurationMs = 900,
            Coverage = 0.9,
            InputPointCount = 10
        };

        PostgresCycleScientificComputeEngine.EnsureEquivalent(
            "temperature",
            feature,
            database.Mean,
            database);
    }

    [Fact]
    public void Verify_RejectsStreamBatchSemanticDrift()
    {
        var feature = Feature(42, 900, 0.9, 10);
        var database = new PostgresCycleScientificComputeEngine.SqlScopeResult
        {
            Mean = 43,
            ValidDurationMs = 900,
            Coverage = 0.9,
            InputPointCount = 10
        };

        Assert.Throws<ScientificComputeMismatchException>(() =>
            PostgresCycleScientificComputeEngine.EnsureEquivalent(
                "temperature",
                feature,
                database.Mean,
                database));
    }

    private static CycleSignalFeature Feature(
        double value,
        double duration,
        double coverage,
        int count)
        => new()
        {
            Code = "mean",
            DefinitionVersion = 1,
            DefinitionHash = new string('a', 64),
            ComputationHash = new string('b', 64),
            Value = value,
            ValidDurationMs = duration,
            Coverage = coverage,
            InputPointCount = count
        };
}
