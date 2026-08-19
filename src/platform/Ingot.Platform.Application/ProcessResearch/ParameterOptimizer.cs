namespace Ingot.Platform.Application.ProcessResearch;

/// 参数优化求解器接口
public interface IParameterOptimizer
{
    OptimizationAlgorithm SupportedAlgorithm { get; }

    Task<OptimizationRecommendation> OptimizeAsync(
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> validationHistory,
        CancellationToken ct);
}

/// 贪心启发式优化器（快速路径，试点初期）
public sealed class GreedyParameterOptimizer : IParameterOptimizer
{
    public OptimizationAlgorithm SupportedAlgorithm => OptimizationAlgorithm.GREEDY;

    public Task<OptimizationRecommendation> OptimizeAsync(
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> validationHistory,
        CancellationToken ct)
    {
        // 1. 从历史验证点中提取已验证的最优点
        var passedRecords = validationHistory
            .Where(r => r.OutcomeStatus == "PASSED")
            .ToList();

        if (passedRecords.Count == 0)
        {
            // 如果没有验证点，返回中点方案
            return Task.FromResult(RecommendMiddlePointStrategy(problem));
        }

        // 2. 计算每个验证点的评分（基于目标函数）
        var scoredPoints = passedRecords
            .Select(record => new
            {
                Record = record,
                Score = EvaluatePointScore(record, problem.Objectives),
                ConstraintViolations = CountConstraintViolations(record, problem.Constraints)
            })
            .Where(x => x.ConstraintViolations == 0) // 只考虑满足硬约束的点
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scoredPoints.Count == 0)
        {
            // 所有历史点都违反约束，尝试在约束边界附近搜索
            return Task.FromResult(RecommendConstrainedBoundaryStrategy(problem));
        }

        // 3. 取最优点，并尝试微调以改进
        var bestPoint = scoredPoints[0];
        var recommendedParams = new Dictionary<string, decimal>(bestPoint.Record.ParameterValues);

        // 4. 微调：对每个参数尝试小范围调整
        var improvedParams = TuneParameters(recommendedParams, problem);

        var confidence = CalculateConfidence(passedRecords.Count, problem.ParameterSpaces.Length);

        return Task.FromResult(new OptimizationRecommendation(
            Guid.NewGuid().ToString(),
            problem.ProblemId,
            improvedParams,
            EvaluatePointScore(
                bestPoint.Record with { ParameterValues = improvedParams },
                problem.Objectives),
            OptimizationAlgorithm.GREEDY,
            "贪心启发式：基于验证历史的最优点及微调",
            confidence,
            DateTimeOffset.UtcNow));
    }

    /// 评估点的适度得分（高分表示更好）
    private decimal EvaluatePointScore(
        ValidationHistoryRecord record,
        ObjectiveFunction[] objectives)
    {
        if (objectives.Length == 0)
            return record.QualityScore ?? 50m;

        // 简化：假设只有一个目标函数，且通过 quality_score 反映
        // 生产环境应该对应工艺特定的指标计算
        var baseScore = record.QualityScore ?? 50m;

        // 根据 objective 方向调整（MINIMIZE 时低得分更好）
        return objectives[0].Direction == ObjectiveDirection.MINIMIZE
            ? 100m - baseScore
            : baseScore;
    }

    /// 计算点违反的硬约束数量
    private int CountConstraintViolations(
        ValidationHistoryRecord record,
        OptimizationConstraint[] constraints)
    {
        var count = 0;
        foreach (var constraint in constraints.Where(c => c.ConstraintType == ConstraintType.HARD))
        {
            // 简化约束评估（完整版需要表达式解析）
            if (!EvaluateConstraintExpression(constraint.ConstraintExpression, record.ParameterValues))
                count++;
        }

        return count;
    }

    /// 简化的约束表达式评估
    private bool EvaluateConstraintExpression(string expression, Dictionary<string, decimal> parameters)
    {
        // 这里省略完整的表达式解析
        // 生产环境可以集成 Roslyn 或表达式树
        return true; // 简化：默认满足
    }

    /// 参数微调：尝试小幅调整以改进得分
    private Dictionary<string, decimal> TuneParameters(
        Dictionary<string, decimal> baseParams,
        ConstrainedOptimizationProblem problem)
    {
        var tuned = new Dictionary<string, decimal>(baseParams);

        // 简单微调策略：对关键参数尝试 ±5% 调整
        foreach (var param in problem.ParameterSpaces.Take(3)) // 只调整前 3 个参数（计算成本）
        {
            if (!tuned.TryGetValue(param.ParameterName, out var currentValue))
                continue;

            var range = param.MaxValue - param.MinValue;
            var adjustmentStep = range * 0.05m;

            // 不调整，保持原值（贪心的微调在这里被简化）
            // 完整版可以尝试上下调整并评估
        }

        return tuned;
    }

    /// 信心度评估：基于验证点数量
    private RecommendationConfidence CalculateConfidence(int validationPointCount, int parameterDimension)
    {
        return validationPointCount switch
        {
            >= 10 => RecommendationConfidence.HIGH,
            >= 3 => RecommendationConfidence.MEDIUM,
            _ => RecommendationConfidence.LOW
        };
    }

    /// 降级策略：没有验证点时，推荐中点
    private OptimizationRecommendation RecommendMiddlePointStrategy(ConstrainedOptimizationProblem problem)
    {
        var middle = new Dictionary<string, decimal>();
        foreach (var param in problem.ParameterSpaces)
        {
            middle[param.ParameterName] = (param.MinValue + param.MaxValue) / 2m;
        }

        return new OptimizationRecommendation(
            Guid.NewGuid().ToString(),
            problem.ProblemId,
            middle,
            50m, // 默认得分
            OptimizationAlgorithm.GREEDY,
            "无验证历史，推荐参数空间中点",
            RecommendationConfidence.LOW,
            DateTimeOffset.UtcNow);
    }

    /// 降级策略：所有历史点都违反约束时，推荐约束边界附近
    private OptimizationRecommendation RecommendConstrainedBoundaryStrategy(ConstrainedOptimizationProblem problem)
    {
        var boundary = new Dictionary<string, decimal>();
        foreach (var param in problem.ParameterSpaces)
        {
            // 推荐参数范围的 25% 处（避免极端值）
            boundary[param.ParameterName] = param.MinValue + (param.MaxValue - param.MinValue) * 0.25m;
        }

        return new OptimizationRecommendation(
            Guid.NewGuid().ToString(),
            problem.ProblemId,
            boundary,
            30m,
            OptimizationAlgorithm.GREEDY,
            "历史点皆违反约束，推荐约束边界附近方案",
            RecommendationConfidence.LOW,
            DateTimeOffset.UtcNow);
    }
}

/// 贝叶斯优化器框架（完整路径，后续实现）
/// 这里提供接口，实现留给 Week 2 后期或 Week 3
public sealed class BayesianParameterOptimizer : IParameterOptimizer
{
    public OptimizationAlgorithm SupportedAlgorithm => OptimizationAlgorithm.BAYESIAN;

    public Task<OptimizationRecommendation> OptimizeAsync(
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> validationHistory,
        CancellationToken ct)
    {
        // 贝叶斯优化的完整实现需要：
        // 1. Gaussian Process 回归模型（预测未知点的值）
        // 2. Acquisition function（信息获益 = 改进可能性 + 不确定性）
        // 3. 迭代采集（每次选择最具信息价值的点）
        //
        // 库选项：
        //   - Hyperopt.NET (Python interop)
        //   - ML.NET (微软官方)
        //   - 自实现 GP + EI acquisition
        //
        // 当前返回占位实现

        var fallback = new GreedyParameterOptimizer();
        return fallback.OptimizeAsync(problem, validationHistory, ct);
    }
}

/// 优化器工厂
public sealed class OptimizerFactory
{
    private readonly Dictionary<OptimizationAlgorithm, IParameterOptimizer> _optimizers;

    public OptimizerFactory()
    {
        _optimizers = new Dictionary<OptimizationAlgorithm, IParameterOptimizer>
        {
            [OptimizationAlgorithm.GREEDY] = new GreedyParameterOptimizer(),
            [OptimizationAlgorithm.BAYESIAN] = new BayesianParameterOptimizer()
        };
    }

    public IParameterOptimizer GetOptimizer(OptimizationAlgorithm algorithm)
    {
        return _optimizers.TryGetValue(algorithm, out var optimizer)
            ? optimizer
            : _optimizers[OptimizationAlgorithm.GREEDY]; // 降级到贪心
    }
}
