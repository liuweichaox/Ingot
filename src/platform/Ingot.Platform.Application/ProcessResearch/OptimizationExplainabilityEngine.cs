namespace Ingot.Platform.Application.ProcessResearch;

/// 优化结果可解释性引擎
/// 为推荐方案提供：为什么、灵敏度、风险、对标
public interface IOptimizationExplainabilityEngine
{
    OptimizationExplanation[] ExplainParameters(
        OptimizationRecommendation recommendation,
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> validationHistory);

    OptimizationRisk[] AssessRisks(
        OptimizationRecommendation recommendation,
        ConstrainedOptimizationProblem problem,
        ConvexHullBoundary[] operatingRegionBoundaries);

    OptimizationBenchmark? CompareToCurrent(
        OptimizationRecommendation recommendation,
        ValidationHistoryRecord? currentBestRecord);
}

public sealed class OptimizationExplainabilityEngine : IOptimizationExplainabilityEngine
{
    public OptimizationExplanation[] ExplainParameters(
        OptimizationRecommendation recommendation,
        ConstrainedOptimizationProblem problem,
        IReadOnlyList<ValidationHistoryRecord> validationHistory)
    {
        var explanations = new List<OptimizationExplanation>();

        // 灵敏度分析：参数对目标函数的影响程度
        foreach (var param in problem.ParameterSpaces)
        {
            var sensitivity = CalculateParameterSensitivity(
                param,
                recommendation.RecommendedParameters,
                problem.Objectives,
                validationHistory);

            var role = ClassifyParameterRole(sensitivity);
            var impact = EstimateImpactIfChanged(param, recommendation.RecommendedParameters);

            explanations.Add(new OptimizationExplanation(
                recommendation.RecommendationId,
                param.ParameterName,
                sensitivity,
                role,
                impact));
        }

        return explanations.ToArray();
    }

    public OptimizationRisk[] AssessRisks(
        OptimizationRecommendation recommendation,
        ConstrainedOptimizationProblem problem,
        ConvexHullBoundary[] operatingRegionBoundaries)
    {
        var risks = new List<OptimizationRisk>();

        // 风险 1：推荐点未在验证历史中出现
        var isUnvalidatedPoint = CheckIfUnvalidatedRegion(
            recommendation.RecommendedParameters,
            operatingRegionBoundaries);
        if (isUnvalidatedPoint.IsFlagged)
        {
            risks.Add(new OptimizationRisk(
                recommendation.RecommendationId,
                OptimizationRiskType.UNVALIDATED_REGION,
                $"推荐点位于未验证区域：{string.Join(", ", isUnvalidatedPoint.UnvalidatedParams)}",
                isUnvalidatedPoint.SeverityScore));
        }

        // 风险 2：接近约束边界
        var constraintMarginRisk = CheckConstraintMargins(
            recommendation.RecommendedParameters,
            problem.Constraints);
        if (constraintMarginRisk != null)
        {
            risks.Add(constraintMarginRisk);
        }

        // 风险 3：超出训练数据范围（推外插）
        var extrapolationRisk = CheckExtrapolation(
            recommendation.RecommendedParameters,
            problem.ParameterSpaces);
        if (extrapolationRisk != null)
        {
            risks.Add(extrapolationRisk);
        }

        // 风险 4：多目标冲突
        if (problem.Objectives.Length > 1)
        {
            var conflictRisk = CheckObjectiveConflicts(recommendation, problem);
            if (conflictRisk != null)
                risks.Add(conflictRisk);
        }

        return risks.ToArray();
    }

    public OptimizationBenchmark? CompareToCurrent(
        OptimizationRecommendation recommendation,
        ValidationHistoryRecord? currentBestRecord)
    {
        if (currentBestRecord == null)
            return null;

        var currentScore = currentBestRecord.QualityScore ?? 50m;
        var recommendedScore = recommendation.PredictedObjectiveValue;
        var improvementRatio = (recommendedScore - currentScore) / Math.Abs(currentScore);

        return new OptimizationBenchmark(
            recommendation.RecommendationId,
            currentBestRecord.ValidationHistoryId,
            currentScore,
            recommendedScore,
            (decimal)improvementRatio);
    }

    /// 计算参数的灵敏度（0-1）
    private decimal CalculateParameterSensitivity(
        ParameterSpace param,
        Dictionary<string, decimal> baselineParams,
        ObjectiveFunction[] objectives,
        IReadOnlyList<ValidationHistoryRecord> validationHistory)
    {
        // 简化：基于验证历史中该参数的变异程度
        var relevantRecords = validationHistory
            .Where(r => r.ParameterValues.TryGetValue(param.ParameterName, out _))
            .ToList();

        if (relevantRecords.Count < 2)
            return 0.33m; // 数据不足，默认中等影响

        var values = relevantRecords
            .Select(r => r.ParameterValues[param.ParameterName])
            .ToList();

        var variance = CalculateVariance(values);
        var range = param.MaxValue - param.MinValue;
        var normalizedVariance = variance / (range * range);

        return Math.Min(1m, (decimal)normalizedVariance * 10); // 调整系数
    }

    /// 对参数进行分类：关键、辅助、无关
    private string ClassifyParameterRole(decimal sensitivity)
    {
        return sensitivity switch
        {
            >= 0.7m => "关键参数（强影响）",
            >= 0.3m => "辅助参数（中等影响）",
            _ => "无关参数（弱影响）"
        };
    }

    /// 估计改变参数的影响
    private decimal? EstimateImpactIfChanged(
        ParameterSpace param,
        Dictionary<string, decimal> recommendedParamValues)
    {
        // 简化：假设参数在中点处，估计改变 10% 的影响
        if (!recommendedParamValues.TryGetValue(param.ParameterName, out var current))
            return null;

        var range = param.MaxValue - param.MinValue;
        var changeAmount = range * 0.1m;

        // 影响程度与参数当前位置有关（边界附近影响更大）
        var distanceToMin = current - param.MinValue;
        var distanceToMax = param.MaxValue - current;
        var marginRatio = Math.Min(distanceToMin, distanceToMax) / range;

        return changeAmount * (1 - (decimal)marginRatio); // 边界附近影响更大
    }

    private record UnvalidatedPointCheck(bool IsFlagged, decimal SeverityScore, string[] UnvalidatedParams);

    /// 检查是否为未验证区域
    private UnvalidatedPointCheck CheckIfUnvalidatedRegion(
        Dictionary<string, decimal> parameterValues,
        ConvexHullBoundary[] boundaries)
    {
        var unvalidatedParams = new List<string>();

        foreach (var boundary in boundaries)
        {
            if (!parameterValues.TryGetValue(boundary.ParameterName, out var value))
                continue;

            // 如果参数值在边界附近但未被验证点完全覆盖
            var marginFromMin = value - boundary.MinValue;
            var marginFromMax = boundary.MaxValue - value;
            var isAtEdge = marginFromMin < (boundary.MaxValue - boundary.MinValue) * 0.15m ||
                           marginFromMax < (boundary.MaxValue - boundary.MinValue) * 0.15m;

            if (isAtEdge && boundary.CoverageConfidence < 0.7m)
            {
                unvalidatedParams.Add(boundary.ParameterName);
            }
        }

        return new UnvalidatedPointCheck(
            unvalidatedParams.Count > 0,
            unvalidatedParams.Count > 0 ? 0.6m : 0m,
            unvalidatedParams.ToArray());
    }

    /// 检查约束边界余量
    private OptimizationRisk? CheckConstraintMargins(
        Dictionary<string, decimal> parameterValues,
        OptimizationConstraint[] constraints)
    {
        // 简化：检查是否接近约束边界（需要约束表达式解析）
        // 这里省略完整实现，返回 null 表示无风险
        return null;
    }

    /// 检查是否超出训练数据范围（推外插）
    private OptimizationRisk? CheckExtrapolation(
        Dictionary<string, decimal> parameterValues,
        ParameterSpace[] parameterSpaces)
    {
        foreach (var param in parameterSpaces)
        {
            if (!parameterValues.TryGetValue(param.ParameterName, out var value))
                continue;

            if (value < param.MinValue || value > param.MaxValue)
            {
                return new OptimizationRisk(
                    Guid.NewGuid().ToString(),
                    OptimizationRiskType.EXTRAPOLATION,
                    $"参数 {param.ParameterName} 超出定义范围：{value} (允许 {param.MinValue}-{param.MaxValue})",
                    0.5m);
            }
        }

        return null;
    }

    /// 检查多目标间的冲突
    private OptimizationRisk? CheckObjectiveConflicts(
        OptimizationRecommendation recommendation,
        ConstrainedOptimizationProblem problem)
    {
        if (problem.Objectives.Length <= 1)
            return null;

        // 简化：如果有多个目标且方向不同，可能存在冲突
        var directions = problem.Objectives.Select(o => o.Direction).Distinct().Count();
        if (directions > 1)
        {
            return new OptimizationRisk(
                recommendation.RecommendationId,
                OptimizationRiskType.CONFLICTING_OBJECTIVES,
                $"存在 {directions} 个相互冲突的目标函数（某些目标可能无法同时最优化）",
                0.4m);
        }

        return null;
    }

    private decimal CalculateVariance(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
            return 0;

        var mean = values.Average();
        var squaredDifferences = values.Select(v => (v - mean) * (v - mean));
        return squaredDifferences.Average();
    }
}
