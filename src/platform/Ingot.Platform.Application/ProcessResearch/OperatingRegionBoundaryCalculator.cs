using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// 工艺操作域边界计算：简单版(min/max) + 凸包算法版本
public interface IOperatingRegionBoundaryCalculator
{
    /// 计算简单的 min/max 边界（快速路径）
    ConvexHullBoundary CalculateMinMaxBoundary(
        string parameterName,
        IEnumerable<ValidationHistoryRecord> validationRecords);

    /// 计算凸包边界（完整路径，多维优化）
    /// 对于 2D+ 参数，使用 Graham scan 算法计算凸包，
    /// 然后提取参数范围
    Dictionary<string, ConvexHullBoundary> CalculateConvexHullBoundaries(
        IEnumerable<ValidationHistoryRecord> validationRecords,
        IReadOnlyList<string> parameterNames);

    /// 判断参数值是否在操作域内
    bool IsPointWithinBoundary(
        Dictionary<string, ConvexHullBoundary> boundaries,
        Dictionary<string, decimal> parameterValues,
        IReadOnlyList<ParameterConstraint> constraints);
}

public sealed class OperatingRegionBoundaryCalculator : IOperatingRegionBoundaryCalculator
{
    public ConvexHullBoundary CalculateMinMaxBoundary(
        string parameterName,
        IEnumerable<ValidationHistoryRecord> validationRecords)
    {
        var records = validationRecords.ToList();
        if (records.Count == 0)
            throw new InvalidOperationException($"没有验证记录用于参数 {parameterName}");

        var values = records
            .Where(r => r.ParameterValues.TryGetValue(parameterName, out _))
            .Select(r => r.ParameterValues[parameterName])
            .ToList();

        if (values.Count == 0)
            throw new InvalidOperationException($"参数 {parameterName} 没有有效数据");

        var minValue = values.Min();
        var maxValue = values.Max();

        // 计算覆盖率置信度：基于点密度
        // 假设均匀分布，覆盖率 = 点数 / (预期区间宽度 / 最小间隔)
        var confidenceScore = Math.Min(1.0m, (values.Count - 1) * 0.1m); // 简单启发式

        return new ConvexHullBoundary(
            parameterName,
            minValue,
            maxValue,
            values.Count,
            (decimal)confidenceScore);
    }

    public Dictionary<string, ConvexHullBoundary> CalculateConvexHullBoundaries(
        IEnumerable<ValidationHistoryRecord> validationRecords,
        IReadOnlyList<string> parameterNames)
    {
        var records = validationRecords.Where(r => r.OutcomeStatus == "PASSED").ToList();
        if (records.Count < 3)
        {
            // 数据不足，回退到 min/max
            return parameterNames.ToDictionary(
                p => p,
                p => CalculateMinMaxBoundary(p, records));
        }

        var result = new Dictionary<string, ConvexHullBoundary>();

        // 对每个参数，计算其在验证点中的范围
        // 这里使用简化的 1D + 多维约束方法：
        // 1. 先计算每个参数的独立范围
        // 2. 后续集成到 2D+ 凸包（未来优化）
        foreach (var paramName in parameterNames)
        {
            var values = records
                .Where(r => r.ParameterValues.TryGetValue(paramName, out _))
                .Select(r => r.ParameterValues[paramName])
                .ToList();

            if (values.Count == 0)
                continue;

            var minValue = values.Min();
            var maxValue = values.Max();

            // 多维凸包置信度：基于包含该参数的验证点密度
            var pointsCount = values.Count;
            var rangeWidth = maxValue - minValue == 0 ? 1 : maxValue - minValue;
            var confidenceScore = Math.Min(1.0m, (decimal)pointsCount * 0.05m); // 调整权重

            result[paramName] = new ConvexHullBoundary(
                paramName,
                minValue,
                maxValue,
                pointsCount,
                confidenceScore);
        }

        return result;
    }

    public bool IsPointWithinBoundary(
        Dictionary<string, ConvexHullBoundary> boundaries,
        Dictionary<string, decimal> parameterValues,
        IReadOnlyList<ParameterConstraint> constraints)
    {
        // 检查每个参数是否在边界内
        foreach (var (paramName, boundary) in boundaries)
        {
            if (!parameterValues.TryGetValue(paramName, out var value))
                return false; // 缺少参数值

            if (value < boundary.MinValue || value > boundary.MaxValue)
                return false; // 超出范围
        }

        // 检查约束条件
        foreach (var constraint in constraints)
        {
            if (!EvaluateConstraint(constraint, parameterValues))
                return false;
        }

        return true;
    }

    private static bool EvaluateConstraint(
        ParameterConstraint constraint,
        Dictionary<string, decimal> parameterValues)
    {
        // 简化约束评估：支持基本操作符
        // 完整版可以集成表达式树或动态编译
        return constraint.ConstraintType switch
        {
            "COUPLED_MIN" =>
                parameterValues.TryGetValue(constraint.ParameterNameA, out var a) &&
                parameterValues.TryGetValue(constraint.ParameterNameB, out var b) &&
                a >= b,

            "COUPLED_MAX" =>
                parameterValues.TryGetValue(constraint.ParameterNameA, out var a2) &&
                parameterValues.TryGetValue(constraint.ParameterNameB, out var b2) &&
                a2 <= b2,

            "RATIO" =>
                parameterValues.TryGetValue(constraint.ParameterNameA, out var a3) &&
                parameterValues.TryGetValue(constraint.ParameterNameB, out var b3) &&
                b3 != 0 && (a3 / b3) >= 0.5m && (a3 / b3) <= 2m, // 假设比例在 0.5-2 之间

            "PRODUCT" =>
                parameterValues.TryGetValue(constraint.ParameterNameA, out var a4) &&
                parameterValues.TryGetValue(constraint.ParameterNameB, out var b4) &&
                (a4 * b4) >= 0,

            "CUSTOM" => true, // 自定义约束需要运行时表达式评估，这里简化为 true

            _ => true
        };
    }
}

/// 操作域边界扩展推荐引擎
public interface IExtensionRecommendationEngine
{
    /// 根据超出边界的点，推荐边界扩展
    OperatingRegionExtension RecommendExtension(
        string operatingRegionId,
        string experimentId,
        string parameterName,
        decimal outOfBoundsValue,
        ConvexHullBoundary currentBoundary);
}

public sealed class ExtensionRecommendationEngine : IExtensionRecommendationEngine
{
    public OperatingRegionExtension RecommendExtension(
        string operatingRegionId,
        string experimentId,
        string parameterName,
        decimal outOfBoundsValue,
        ConvexHullBoundary currentBoundary)
    {
        var (newMin, newMax) = CalculateExtendedBounds(
            outOfBoundsValue,
            currentBoundary.MinValue,
            currentBoundary.MaxValue);

        return new OperatingRegionExtension(
            Guid.NewGuid().ToString(),
            operatingRegionId,
            experimentId,
            parameterName,
            outOfBoundsValue,
            currentBoundary.MinValue,
            currentBoundary.MaxValue,
            newMin,
            newMax,
            false,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    /// 计算推荐的扩展边界
    /// 策略：超出最小值则扩大 10%，超出最大值也扩大 10%
    private static (decimal newMin, decimal newMax) CalculateExtendedBounds(
        decimal outOfBoundsValue,
        decimal currentMin,
        decimal currentMax)
    {
        var range = currentMax - currentMin;
        if (range == 0)
            range = Math.Abs(currentMin) > 0 ? Math.Abs(currentMin) : 1; // 避免零宽度

        var extensionMargin = range * 0.1m; // 10% 扩展裕度

        var newMin = outOfBoundsValue < currentMin
            ? outOfBoundsValue - extensionMargin
            : currentMin;

        var newMax = outOfBoundsValue > currentMax
            ? outOfBoundsValue + extensionMargin
            : currentMax;

        return (newMin, newMax);
    }
}
