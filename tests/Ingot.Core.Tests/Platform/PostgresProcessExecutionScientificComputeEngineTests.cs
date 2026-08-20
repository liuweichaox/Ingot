// 验证平台组件 PostgresProcessExecutionScientificComputeEngine 的成功、拒绝和安全边界。

using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PostgresProcessExecutionScientificComputeEngineTests
{
    [Fact]
    public void Verify_AcceptsNumericallyEquivalentDatabaseFeature()
    {
        var feature = Feature(42, 900, 0.9, 10);
        var database = new PostgresProcessExecutionScientificComputeEngine.SqlScopeResult
        {
            Mean = 42 + 1e-11,
            ValidDurationMs = 900,
            Coverage = 0.9,
            InputPointCount = 10
        };

        PostgresProcessExecutionScientificComputeEngine.EnsureEquivalent(
            "temperature",
            feature,
            database.Mean,
            database);
    }

    [Fact]
    public void Verify_RejectsStreamBatchSemanticDrift()
    {
        var feature = Feature(42, 900, 0.9, 10);
        var database = new PostgresProcessExecutionScientificComputeEngine.SqlScopeResult
        {
            Mean = 43,
            ValidDurationMs = 900,
            Coverage = 0.9,
            InputPointCount = 10
        };

        Assert.Throws<ScientificComputeMismatchException>(() =>
            PostgresProcessExecutionScientificComputeEngine.EnsureEquivalent(
                "temperature",
                feature,
                database.Mean,
                database));
    }

    private static ProcessSignalFeature Feature(
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
