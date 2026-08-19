namespace Ingot.Platform.Application.ProcessResearch;

/// Week 2: 参数优化引擎
/// 将工艺优化问题建模为约束优化，支持多个目标函数和约束

/// 目标函数定义：最小化/最大化某个工艺指标
public record ObjectiveFunction(
    string ObjectiveName,
    ObjectiveDirection Direction, // MINIMIZE 或 MAXIMIZE
    string? Description);

public enum ObjectiveDirection
{
    MINIMIZE, // 降低成本、减少缺陷率
    MAXIMIZE  // 提升产率、改善质量评分
}

/// 参数空间定义：参数的允许范围
public record ParameterSpace(
    string ParameterName,
    decimal MinValue,
    decimal MaxValue,
    ParameterType ParameterType, // CONTINUOUS, DISCRETE, CATEGORICAL
    string? Unit);

public enum ParameterType
{
    CONTINUOUS,   // 连续值（温度、压力）
    DISCRETE,     // 离散值（步数、周期次数）
    CATEGORICAL   // 分类值（工艺路径 A/B/C）
}

/// 优化约束条件
public record OptimizationConstraint(
    string ConstraintName,
    ConstraintType ConstraintType, // HARD 硬约束，SOFT 软约束
    string ConstraintExpression, // e.g., "temp >= pressure * 2"
    string? ViolationPenalty); // 如果是软约束，违反时的惩罚

public enum ConstraintType
{
    HARD, // 违反则拒绝方案
    SOFT  // 违反则降低得分（惩罚）
}

/// 完整的约束优化问题定义
public record ConstrainedOptimizationProblem(
    string ProblemId,
    string OperatingRegionId,
    ObjectiveFunction[] Objectives,
    ParameterSpace[] ParameterSpaces,
    OptimizationConstraint[] Constraints,
    OptimizationAlgorithm PreferredAlgorithm, // GREEDY, BAYESIAN, EVOLUTIONARY
    DateTimeOffset CreatedAt);

public enum OptimizationAlgorithm
{
    GREEDY,       // 贪心启发式（快速，适合初期）
    BAYESIAN,     // 贝叶斯优化（精准，需要历史数据）
    EVOLUTIONARY  // 进化算法（并行，多目标优化）
}

/// 优化推荐方案
public record OptimizationRecommendation(
    string RecommendationId,
    string ProblemId,
    Dictionary<string, decimal> RecommendedParameters,
    decimal PredictedObjectiveValue, // 预测目标函数值
    OptimizationAlgorithm AlgorithmUsed,
    string? ExplanationSummary, // 为什么推荐这个方案
    RecommendationConfidence Confidence, // 置信度
    DateTimeOffset CreatedAt);

public enum RecommendationConfidence
{
    LOW,      // 数据不足，推荐不可靠
    MEDIUM,   // 中等置信，可尝试
    HIGH      // 高度置信，强烈推荐
}

/// 优化推荐的详细解释
public record OptimizationExplanation(
    string RecommendationId,
    string ParameterName,
    decimal SensitivityScore, // 参数对目标函数的影响程度 (0-1)
    string RoleDescription, // "关键参数"、"辅助参数"、"无关参数"
    decimal? EstimatedImpactIfChanged); // 如果改变这个参数，目标函数预期变化

/// 风险评估
public record OptimizationRisk(
    string RecommendationId,
    OptimizationRiskType RiskType,
    string RiskDescription,
    decimal SeverityScore); // 0-1，越高越严重

public enum OptimizationRiskType
{
    UNVALIDATED_REGION,     // 推荐点未在验证历史中出现
    CONSTRAINT_MARGIN_TIGHT, // 接近约束边界
    EXTRAPOLATION,           // 超出训练数据范围
    CONFLICTING_OBJECTIVES   // 多个目标间的冲突
}

/// 与历史最优的对标
public record OptimizationBenchmark(
    string RecommendationId,
    string CurrentBestExecutionId,
    decimal CurrentBestObjectiveValue,
    decimal RecommendedObjectiveValue,
    decimal ImprovementRatio); // (新-旧) / |旧| 的百分比

/// 优化会话（跟踪迭代过程）
public record OptimizationSession(
    string SessionId,
    string OperatingRegionId,
    OptimizationAlgorithm Algorithm,
    int IterationCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    OptimizationSessionStatus Status); // RUNNING, COMPLETED, FAILED

public enum OptimizationSessionStatus
{
    RUNNING,
    COMPLETED,
    FAILED
}
