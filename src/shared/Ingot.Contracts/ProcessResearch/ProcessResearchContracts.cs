// 定义下一配方建议跨层契约；只承载请求、冻结条件和证据记录，不包含存储或执行逻辑。
namespace Ingot.Contracts.ProcessResearch;

public static class ResearchVariableRoles
{
    public const string Control = "control";
    public const string Process = "process";
    public const string Material = "material";
    public const string Environment = "environment";
    public const string Outcome = "outcome";

    public static bool IsValid(string? value)
        => value is Control or Process or Material or Environment or Outcome;
}

public static class ResearchOptimizationIntents
{
    public const string ReachSpecification = "reach-specification";

    public static bool IsValid(string? value)
        => value is ReachSpecification;
}

/// <summary>建议条件中用于筛选真实生产运行的结构化范围键。</summary>
public static class RecipeRecommendationScopeKeys
{
    public const string ProductFamilyCode = "product_family_code";
    public const string ProductCode = "product_code";
    public const string EquipmentId = "equipment_id";
    public const string ProcessSpecificationId = "process_specification_id";
    public const string ProcessSpecificationVersion = "process_specification_version";
    public const string OutputItemId = "output_item_id";
    public const string LookbackDays = "lookback_days";
}

public sealed record ResearchObjective
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Unit { get; init; }
    public string Direction { get; init; } = "target";
    public double? Baseline { get; init; }
    public required double Target { get; init; }
    public double? LowerLimit { get; init; }
    public double? UpperLimit { get; init; }
    public double Weight { get; init; } = 1;

    public string? DataSource { get; init; }
}

public sealed record ResearchVariable
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Role { get; init; } = ResearchVariableRoles.Control;
    public required string Unit { get; init; }
    public double? LowerLimit { get; init; }
    public double? UpperLimit { get; init; }
    public string? DataSource { get; init; }
}

public sealed record ResearchConstraint
{
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required string VariableCode { get; init; }
    public string Operator { get; init; } = "<=";
    public required double Limit { get; init; }
    public required string Unit { get; init; }
    public bool SafetyCritical { get; init; }
}

public sealed record ResearchOutcomeConstraint
{
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required string OutcomeCode { get; init; }
    public string Operator { get; init; } = "<=";
    public required double Limit { get; init; }
    public required string Unit { get; init; }
    public bool SafetyCritical { get; init; } = true;
    public double MinimumProbability { get; init; } = 0.95;
    public string? DataSource { get; init; }
}

public static class ResearchDerivedFeatureOperators
{
    public const string Identity = "identity";
    public const string Absolute = "absolute";
    public const string Sum = "sum";
    public const string Mean = "mean";
    public const string Product = "product";
    public const string Difference = "difference";
    public const string AbsoluteDifference = "absolute_difference";
    public const string Ratio = "ratio";
    public const string Minimum = "minimum";
    public const string Maximum = "maximum";
    public const string StandardDeviation = "standard_deviation";

    public static bool IsValid(string? value)
        => value is Identity or Absolute or Sum or Mean or Product or Difference
            or AbsoluteDifference or Ratio or Minimum or Maximum or StandardDeviation;
}

public sealed record ResearchDerivedFeature
{
    public required string Name { get; init; }
    public required string Operator { get; init; }
    public IReadOnlyList<string> Inputs { get; init; } = [];
    public double NormalizationOffset { get; init; }
    public double NormalizationScale { get; init; } = 1;
    public double Epsilon { get; init; } = 1e-9;
}

public sealed record ResearchOptimizationFeatureSet
{
    public string FeatureSetId { get; init; } = "generic";
    public int Version { get; init; } = 1;
    public IReadOnlyList<ResearchDerivedFeature> DerivedFeatures { get; init; } = [];
}

/// <summary>
/// 生成一条下一配方建议时使用的全部条件。它属于建议的输入，随建议一同冻结；
/// 不存在可编辑、可激活或可归档的独立范围对象。
/// </summary>
public sealed record RecipeRecommendationBrief
{
    public required string SiteCode { get; init; }
    public required string ProcessSpecificationId { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<ResearchObjective> Objectives { get; init; } = [];
    public IReadOnlyList<ResearchVariable> Variables { get; init; } = [];
    public IReadOnlyList<ResearchConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<ResearchOutcomeConstraint> OutcomeConstraints { get; init; } = [];
    public ResearchOptimizationFeatureSet OptimizationFeatures { get; init; } = new();
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
}

public sealed record ResearchVariableSetting
{
    public required string VariableCode { get; init; }
    public required double Value { get; init; }
    public required string Unit { get; init; }
}

public sealed record OptimizationMetricPrediction
{
    public double Mean { get; init; }
    public double StandardDeviation { get; init; }
    public double Lower95 { get; init; }
    public double Upper95 { get; init; }
    public required string Unit { get; init; }
}

public sealed record OptimizationRunPrediction
{
    public required string ExecutionKey { get; init; }
    public IReadOnlyDictionary<string, OptimizationMetricPrediction> Objectives { get; init; } =
        new Dictionary<string, OptimizationMetricPrediction>();
    public IReadOnlyDictionary<string, OptimizationMetricPrediction> Constraints { get; init; } =
        new Dictionary<string, OptimizationMetricPrediction>();
    public double? FeasibilityProbability { get; init; }
    public double? AcquisitionValue { get; init; }
    public bool ColdStart { get; init; }
    public required string Rationale { get; init; }
}

public sealed record MechanismModelApplicationReference
{
    public required string FusionId { get; init; }
    public int FusionVersion { get; init; }
    public required string FusionHash { get; init; }
    public required string MechanismModelId { get; init; }
    public int MechanismModelVersion { get; init; }
    public required string MechanismModelHash { get; init; }
    public required string FeatureCode { get; init; }
}

public static class ResearchUsefulnessRatings
{
    public const string Useful = "useful";
    public const string PartlyUseful = "partly-useful";
    public const string NotUseful = "not-useful";

    public static bool IsValid(string? value)
        => value is Useful or PartlyUseful or NotUseful;
}

public static class ResearchRecipeRecommendationDecisionStatuses
{
    public const string Accepted = "accepted";
    public const string Modified = "modified";
    public const string Rejected = "rejected";

    public static bool IsValid(string? value)
        => value is Accepted or Modified or Rejected;
}

/// <summary>
/// 工程师对下一配方建议的不可变回执；不构成设备控制命令。
/// </summary>
public sealed record ResearchRecipeRecommendationDecisionRequest
{
    public required string Decision { get; init; }
    public IReadOnlyList<ResearchVariableSetting> EngineerSelectedParameters { get; init; } = [];
    public string? Reason { get; init; }
    public string? UsefulnessRating { get; init; }
}

/// <summary>把已冻结的建议决定关联到一条真实生产运行。</summary>
public sealed record ResearchRecipeRecommendationExecutionLinkRequest
{
    public required string ActualExecutionKey { get; init; }
}

/// <summary>
/// 从实际工艺执行、参数回读和检验记录冻结的建议结果。
/// </summary>
public sealed record ResearchRecipeRecommendationOutcome
{
    public required string ActualExecutionKey { get; init; }
    public required string BriefHash { get; init; }
    public IReadOnlyList<ResearchVariableSetting> ActualParameters { get; init; } = [];
    public IReadOnlyDictionary<string, double> SettingDeviationFromSuggestion { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> SettingDeviationFromEngineerSelection { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> ProcessFeatures { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Outcomes { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> ConstraintOutcomes { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, string> ActualContextSnapshot { get; init; } =
        new Dictionary<string, string>();
    public bool ValidForOptimization { get; init; }
    public string? ExclusionReason { get; init; }
    public required string SourceContentHash { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}

public sealed record ResearchRecipeRecommendationDecision
{
    public Guid DecisionId { get; init; }
    public Guid RecommendationId { get; init; }
    public required string RecommendationKey { get; init; }
    public required string SiteCode { get; init; }
    public required string ProcessSpecificationId { get; init; }
    public required string BriefHash { get; init; }
    public required string Decision { get; init; }
    /// <summary>由独立的实际运行关联证据提供；决定本体不因此被覆盖。</summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ActualExecutionKey { get; init; }
    public IReadOnlyList<ResearchVariableSetting> SuggestedParameters { get; init; } = [];
    public IReadOnlyList<ResearchVariableSetting> EngineerSelectedParameters { get; init; } = [];
    public required OptimizationRunPrediction Prediction { get; init; }
    public string? Reason { get; init; }
    public string? UsefulnessRating { get; init; }
    public required string DecisionSnapshotHash { get; init; }
    public string DecidedBy { get; init; } = "";
    public DateTimeOffset DecidedAt { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ResearchRecipeRecommendationOutcome? Outcome { get; init; }
}

public static class ResearchRecipeRecommendationFlowStates
{
    public const string PendingDecision = "pending-decision";
    public const string Rejected = "rejected";
    public const string PendingExecution = "pending-execution";
    public const string PendingOutcome = "pending-outcome";
    public const string OutcomeFrozen = "outcome-frozen";
    public const string OutcomeExcluded = "outcome-excluded";
    public const string Stale = "stale";
}

public static class ResearchRecipeRecommendationFlowActions
{
    public const string Decide = "decide";
    public const string LinkExecution = "link-execution";
    public const string MaterializeOutcome = "materialize-outcome";
}

/// <summary>以建议项为分页单位返回决定、运行和结果，避免独立游标造成假未决状态。</summary>
public sealed record ResearchRecipeRecommendationFlow
{
    public required ResearchRecipeRecommendation Recommendation { get; init; }
    public required ResearchRecipeRecommendationItem Item { get; init; }
    public ResearchRecipeRecommendationDecision? Decision { get; init; }
    public required string State { get; init; }
    public IReadOnlyList<string> AllowedActions { get; init; } = [];
}

public sealed record ResearchRunObservation
{
    public required string ExecutionKey { get; init; }

    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<ResearchVariableSetting> ActualFactors { get; init; } = [];
    public IReadOnlyDictionary<string, double> SettingDeviationFromPlan { get; init; } =
        new Dictionary<string, double>();
    public bool HasSettingDeviation { get; init; }
    public IReadOnlyDictionary<string, double> ProcessFeatures { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Outcomes { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> ConstraintOutcomes { get; init; } =
        new Dictionary<string, double>();
    public bool ValidForOptimization { get; init; } = true;
    public string? ExclusionReason { get; init; }
    public required string SourceContentHash { get; init; }
}

public sealed record ResearchPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record ResearchRecipeRecommendationRequest
{
    public required RecipeRecommendationBrief Brief { get; init; }
    public int Seed { get; init; }
}

/// <summary>在不调用优化服务的前提下，说明建议条件能装配多少条有效真实运行。</summary>
public sealed record RecipeRecommendationReadiness
{
    public int CandidateRunCount { get; init; }
    public int ValidObservationCount { get; init; }
    public int ExcludedObservationCount { get; init; }
    public bool Truncated { get; init; }
    public IReadOnlyList<string> ObservedExecutionKeys { get; init; } = [];
    public IReadOnlyList<RecipeRecommendationExclusion> ExcludedObservations { get; init; } = [];
}

public sealed record RecipeRecommendationExclusion
{
    public required string ExecutionKey { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// 面向日常生产的下一配方建议。数据来自真实配方运行，不要求用户建立验证计划。
/// </summary>
public sealed record ResearchRecipeRecommendation
{
    public Guid RecommendationId { get; init; }
    public required string SiteCode { get; init; }
    public required string ProcessSpecificationId { get; init; }
    public required RecipeRecommendationBrief Brief { get; init; }
    public required string BriefHash { get; init; }
    public required string ModelVersion { get; init; }
    public required string InputHash { get; init; }
    public int ObservationCount { get; init; }
    public int AutoAssembledObservationCount { get; init; }
    public int ProcessFeatureCount { get; init; }
    public required string FeatureSetId { get; init; }
    public int FeatureSetVersion { get; init; }
    public int DerivedFeatureCount { get; init; }
    public required string MechanismKnowledgeSnapshotHash { get; init; }
    public required string MechanismModelSnapshotHash { get; init; }
    public IReadOnlyList<MechanismModelApplicationReference> MechanismModels { get; init; } = [];
    public IReadOnlyList<ResearchRecipeRecommendationItem> Items { get; init; } = [];
    public bool RequiresEngineerConfirmation { get; init; } = true;
    /// <summary>建议超过该时间后不能再登记新决定，需要重新生成。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ResearchRecipeRecommendationItem
{
    public required string RecommendationKey { get; init; }
    public IReadOnlyList<ResearchVariableSetting> Parameters { get; init; } = [];
    public required OptimizationRunPrediction Prediction { get; init; }
}
