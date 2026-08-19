using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ParameterOptimizerTests
{
    private readonly GreedyParameterOptimizer _optimizer = new();

    [Fact]
    public async Task Optimize_WithValidationHistory_ReturnsRecommendation()
    {
        var validationHistory = new[]
        {
            CreateValidationRecord("exp-1", ("temp", 100m), ("pressure", 50m), "PASSED", 85m),
            CreateValidationRecord("exp-2", ("temp", 120m), ("pressure", 60m), "PASSED", 90m),
            CreateValidationRecord("exp-3", ("temp", 110m), ("pressure", 55m), "PASSED", 88m),
        };

        var problem = CreateOptimizationProblem(new[]
        {
            new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C"),
            new ParameterSpace("pressure", 50m, 100m, ParameterType.CONTINUOUS, "MPa")
        });

        var recommendation = await _optimizer.OptimizeAsync(problem, validationHistory, CancellationToken.None);

        Assert.NotNull(recommendation);
        Assert.Equal(problem.ProblemId, recommendation.ProblemId);
        Assert.Equal(2, recommendation.RecommendedParameters.Count);
        Assert.True(recommendation.PredictedObjectiveValue > 0);
        Assert.NotEqual(RecommendationConfidence.LOW, recommendation.Confidence); // 足够数据应该给出 MEDIUM 或 HIGH
    }

    [Fact]
    public async Task Optimize_WithNoValidationHistory_ReturnsMiddlePointStrategy()
    {
        var emptyHistory = Array.Empty<ValidationHistoryRecord>();

        var problem = CreateOptimizationProblem(new[]
        {
            new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C"),
            new ParameterSpace("pressure", 50m, 100m, ParameterType.CONTINUOUS, "MPa")
        });

        var recommendation = await _optimizer.OptimizeAsync(problem, emptyHistory, CancellationToken.None);

        Assert.Equal(RecommendationConfidence.LOW, recommendation.Confidence);
        Assert.Equal(125m, recommendation.RecommendedParameters["temp"]); // (100 + 150) / 2
        Assert.Equal(75m, recommendation.RecommendedParameters["pressure"]); // (50 + 100) / 2
    }

    [Fact]
    public async Task Optimize_FiltersOutFailedExperiments()
    {
        var mixedHistory = new[]
        {
            CreateValidationRecord("exp-1", ("temp", 100m), ("pressure", 50m), "PASSED", 85m),
            CreateValidationRecord("exp-2", ("temp", 120m), ("pressure", 60m), "FAILED", 30m), // 失败
            CreateValidationRecord("exp-3", ("temp", 110m), ("pressure", 55m), "PASSED", 88m),
        };

        var problem = CreateOptimizationProblem(new[]
        {
            new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C"),
            new ParameterSpace("pressure", 50m, 100m, ParameterType.CONTINUOUS, "MPa")
        });

        var recommendation = await _optimizer.OptimizeAsync(problem, mixedHistory, CancellationToken.None);

        // 应该选择最好的 PASSED 记录（exp-3, 得分 88）
        Assert.True(recommendation.PredictedObjectiveValue >= 88m);
    }

    [Fact]
    public async Task Optimize_SelectsBestScoredPoint()
    {
        var validationHistory = new[]
        {
            CreateValidationRecord("exp-1", ("temp", 100m), ("pressure", 50m), "PASSED", 70m),
            CreateValidationRecord("exp-2", ("temp", 120m), ("pressure", 60m), "PASSED", 95m), // 最好
            CreateValidationRecord("exp-3", ("temp", 110m), ("pressure", 55m), "PASSED", 80m),
        };

        var problem = CreateOptimizationProblem(new[]
        {
            new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C"),
            new ParameterSpace("pressure", 50m, 100m, ParameterType.CONTINUOUS, "MPa")
        });

        var recommendation = await _optimizer.OptimizeAsync(problem, validationHistory, CancellationToken.None);

        // 推荐应该基于最高分的点
        Assert.True(recommendation.PredictedObjectiveValue >= 95m);
    }

    [Fact]
    public async Task Optimize_RespectsBoundaryConstraints()
    {
        var validationHistory = new[]
        {
            CreateValidationRecord("exp-1", ("temp", 100m), ("pressure", 50m), "PASSED", 85m),
            CreateValidationRecord("exp-2", ("temp", 120m), ("pressure", 60m), "PASSED", 90m),
        };

        var problem = CreateOptimizationProblem(new[]
        {
            new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C"),
            new ParameterSpace("pressure", 50m, 100m, ParameterType.CONTINUOUS, "MPa")
        });

        var recommendation = await _optimizer.OptimizeAsync(problem, validationHistory, CancellationToken.None);

        // 推荐参数应该在定义的范围内
        Assert.InRange(recommendation.RecommendedParameters["temp"], 100m, 150m);
        Assert.InRange(recommendation.RecommendedParameters["pressure"], 50m, 100m);
    }

    private static ValidationHistoryRecord CreateValidationRecord(
        string experimentId,
        (string, decimal) param1,
        (string, decimal) param2,
        string outcomeStatus,
        decimal qualityScore)
    {
        var paramDict = new Dictionary<string, decimal>
        {
            [param1.Item1] = param1.Item2,
            [param2.Item1] = param2.Item2
        };

        return new ValidationHistoryRecord(
            Guid.NewGuid().ToString(),
            "region-1",
            experimentId,
            $"exec-{experimentId}",
            DateTimeOffset.UtcNow,
            paramDict,
            outcomeStatus,
            qualityScore,
            null,
            DateTimeOffset.UtcNow);
    }

    private static ConstrainedOptimizationProblem CreateOptimizationProblem(
        ParameterSpace[] parameterSpaces)
    {
        return new ConstrainedOptimizationProblem(
            Guid.NewGuid().ToString(),
            "region-1",
            new[] { new ObjectiveFunction("quality", ObjectiveDirection.MAXIMIZE, "最大化质量评分") },
            parameterSpaces,
            Array.Empty<OptimizationConstraint>(),
            OptimizationAlgorithm.GREEDY,
            DateTimeOffset.UtcNow);
    }
}

public sealed class OptimizationExplainabilityEngineTests
{
    private readonly OptimizationExplainabilityEngine _engine = new();

    [Fact]
    public void ExplainParameters_ReturnsExplanationForEachParameter()
    {
        var recommendation = new OptimizationRecommendation(
            "rec-1",
            "prob-1",
            new Dictionary<string, decimal> { ["temp"] = 120m, ["pressure"] = 60m },
            90m,
            OptimizationAlgorithm.GREEDY,
            "Test",
            RecommendationConfidence.MEDIUM,
            DateTimeOffset.UtcNow);

        var problem = new ConstrainedOptimizationProblem(
            "prob-1",
            "region-1",
            new[] { new ObjectiveFunction("quality", ObjectiveDirection.MAXIMIZE, null) },
            new[]
            {
                new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C"),
                new ParameterSpace("pressure", 50m, 100m, ParameterType.CONTINUOUS, "MPa")
            },
            Array.Empty<OptimizationConstraint>(),
            OptimizationAlgorithm.GREEDY,
            DateTimeOffset.UtcNow);

        var validationHistory = new[]
        {
            new ValidationHistoryRecord("v1", "region-1", "exp-1", "exec-1", DateTimeOffset.UtcNow,
                new Dictionary<string, decimal> { ["temp"] = 110m, ["pressure"] = 55m },
                "PASSED", 85m, null, DateTimeOffset.UtcNow),
            new ValidationHistoryRecord("v2", "region-1", "exp-2", "exec-2", DateTimeOffset.UtcNow,
                new Dictionary<string, decimal> { ["temp"] = 130m, ["pressure"] = 65m },
                "PASSED", 88m, null, DateTimeOffset.UtcNow),
        };

        var explanations = _engine.ExplainParameters(recommendation, problem, validationHistory);

        Assert.Equal(2, explanations.Length);
        Assert.Contains(explanations, e => e.ParameterName == "temp");
        Assert.Contains(explanations, e => e.ParameterName == "pressure");
        Assert.All(explanations, e => Assert.InRange(e.SensitivityScore, 0m, 1m));
        Assert.All(explanations, e => Assert.NotEmpty(e.RoleDescription));
    }

    [Fact]
    public void AssessRisks_ReturnsRisksForUnvalidatedRegion()
    {
        var recommendation = new OptimizationRecommendation(
            "rec-1",
            "prob-1",
            new Dictionary<string, decimal> { ["temp"] = 145m }, // 接近边界
            90m,
            OptimizationAlgorithm.GREEDY,
            "Test",
            RecommendationConfidence.MEDIUM,
            DateTimeOffset.UtcNow);

        var problem = new ConstrainedOptimizationProblem(
            "prob-1",
            "region-1",
            new[] { new ObjectiveFunction("quality", ObjectiveDirection.MAXIMIZE, null) },
            new[] { new ParameterSpace("temp", 100m, 150m, ParameterType.CONTINUOUS, "°C") },
            Array.Empty<OptimizationConstraint>(),
            OptimizationAlgorithm.GREEDY,
            DateTimeOffset.UtcNow);

        var boundaries = new[]
        {
            new ConvexHullBoundary("temp", 100m, 150m, 5, 0.5m) // 低置信度
        };

        var risks = _engine.AssessRisks(recommendation, problem, boundaries);

        // 应该检测到高置信度低但接近边界的风险
        Assert.NotEmpty(risks);
    }

    [Fact]
    public void CompareToCurrent_CalculatesImprovementRatio()
    {
        var recommendation = new OptimizationRecommendation(
            "rec-1",
            "prob-1",
            new Dictionary<string, decimal> { ["temp"] = 120m },
            100m, // 推荐得分
            OptimizationAlgorithm.GREEDY,
            "Test",
            RecommendationConfidence.MEDIUM,
            DateTimeOffset.UtcNow);

        var currentBest = new ValidationHistoryRecord(
            "v1",
            "region-1",
            "exp-1",
            "exec-1",
            DateTimeOffset.UtcNow,
            new Dictionary<string, decimal> { ["temp"] = 110m },
            "PASSED",
            80m, // 当前得分
            null,
            DateTimeOffset.UtcNow);

        var benchmark = _engine.CompareToCurrent(recommendation, currentBest);

        Assert.NotNull(benchmark);
        Assert.Equal(100m, benchmark!.RecommendedObjectiveValue);
        Assert.Equal(80m, benchmark.CurrentBestObjectiveValue);
        Assert.True(benchmark.ImprovementRatio > 0); // 改进
    }

    [Fact]
    public void CompareToCurrent_ReturnsNullWhenNoCurrent()
    {
        var recommendation = new OptimizationRecommendation(
            "rec-1",
            "prob-1",
            new Dictionary<string, decimal>(),
            90m,
            OptimizationAlgorithm.GREEDY,
            "Test",
            RecommendationConfidence.MEDIUM,
            DateTimeOffset.UtcNow);

        var benchmark = _engine.CompareToCurrent(recommendation, null);

        Assert.Null(benchmark);
    }
}
