namespace Ingot.Platform.Application.ProcessResearch;

/// 工艺操作域完整实现 (Week 1)

public record ParameterBounds(
    string ParameterBoundsId,
    string OperatingRegionId,
    string ParameterName,
    decimal MinValue,
    decimal MaxValue,
    string? UnitOfMeasure,
    int CriticalityLevel,
    string? CorrelationNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ValidationHistoryRecord(
    string ValidationHistoryId,
    string OperatingRegionId,
    string ExperimentId,
    string ExecutionId,
    DateTimeOffset ValidationTimestamp,
    Dictionary<string, decimal> ParameterValues,
    string OutcomeStatus, // PASSED, FAILED, UNCERTAIN
    decimal? QualityScore,
    string? Notes,
    DateTimeOffset CreatedAt);

public record OperatingRegionExtension(
    string ExtensionId,
    string OperatingRegionId,
    string TriggeringExperimentId,
    string OutOfBoundsParameterName,
    decimal OutOfBoundsValue,
    decimal OriginalMinValue,
    decimal OriginalMaxValue,
    decimal ExtendedMinValue,
    decimal ExtendedMaxValue,
    bool ExtensionApproved,
    string? ApprovedBy,
    DateTimeOffset? ApprovalTimestamp,
    string? ApprovalNotes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ParameterConstraint(
    string ConstraintId,
    string OperatingRegionId,
    string ConstraintType, // COUPLED_MIN, COUPLED_MAX, RATIO, PRODUCT, CUSTOM
    string ParameterNameA,
    string ParameterNameB,
    string ConstraintExpression, // e.g., "temp > pressure * 2"
    string? ConstraintDescription,
    DateTimeOffset CreatedAt);

/// 在边界计算中使用的验证点（多维参数向量）
public record ValidationPoint(
    string ParameterName,
    decimal Value,
    string OutcomeStatus, // PASSED, FAILED, UNCERTAIN
    decimal? QualityScore);

/// 凸包边界计算结果
public record ConvexHullBoundary(
    string ParameterName,
    decimal MinValue,
    decimal MaxValue,
    int PointsUsed,
    decimal CoverageConfidence); // 0-1：基于点密度的覆盖率

/// 操作域置信度枚举
public enum OperatingRegionConfidenceLevel
{
    INCOMPLETE,    // 数据不足，边界可能不完整
    PROVISIONAL,   // 初步边界，基于少量验证点
    VALIDATED,     // 充分验证，边界稳定
    MATURE         // 成熟状态，长期验证稳定
}

/// 边界计算方法
public enum BoundaryCalculationMethod
{
    MIN_MAX,       // 简单min/max，各参数独立
    CONVEX_HULL,   // 凸包算法，多维边界
    ML_MODEL       // 机器学习模型，未来升级
}
