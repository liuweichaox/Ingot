// 定义工艺研发跨层契约；只承载状态、请求和证据快照，不包含存储或执行逻辑。
using System.Text.Json;
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Contracts.ProcessResearch;

public static class ResearchProjectStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Archived = "archived";

    public static bool IsValid(string? value)
        => value is Draft or Active or Completed or Archived;
}

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

public static class ResearchHypothesisStatuses
{
    public const string Proposed = "proposed";
    public const string Selected = "selected";
    public const string Supported = "supported";
    public const string Validated = "validated";
    public const string Rejected = "rejected";
    public const string Inconclusive = "inconclusive";

    public static bool IsValid(string? value)
        => value is Proposed or Selected or Supported or Validated or Rejected or Inconclusive;
}

public static class OperatingRegionStatuses
{
    public const string Candidate = "candidate";
    public const string Validated = "validated";
    public const string Superseded = "superseded";

    public static bool IsValid(string? value)
        => value is Candidate or Validated or Superseded;
}

public static class ResearchKnowledgeStatuses
{
    public const string Draft = "draft";
    public const string Reviewed = "reviewed";
    public const string Published = "published";
    public const string Retired = "retired";

    public static bool IsValid(string? value)
        => value is Draft or Reviewed or Published or Retired;
}

public static class OperatingRegionValidationLevels
{
    public const string Evidence = "evidence";
    public const string Replay = "replay";
    public const string Laboratory = "laboratory";
    public const string Production = "production";

    public static bool IsValid(string? value)
        => value is Evidence or Replay or Laboratory or Production;
}

public static class ResearchHypothesisEffectDirections
{
    public const string Increase = "increase";
    public const string Decrease = "decrease";

    public static bool IsValid(string? value)
        => value is Increase or Decrease;
}

public static class ResearchOptimizationIntents
{
    public const string ReachSpecification = "reach-specification";

    public static bool IsValid(string? value)
        => value is ReachSpecification;
}

public static class ResearchTransferAssessmentStatuses
{
    public const string Recorded = "recorded";
    public const string Reviewed = "reviewed";

    public static bool IsValid(string? value)
        => value is Recorded or Reviewed;
}

public static class ResearchTransferOutcomes
{
    public const string Beneficial = "beneficial";
    public const string Neutral = "neutral";
    public const string NegativeTransfer = "negative-transfer";
    public const string InsufficientEvidence = "insufficient-evidence";

    public static bool IsValid(string? value)
        => value is Beneficial or Neutral or NegativeTransfer or InsufficientEvidence;
}

public static class ResearchConfidenceMethods
{
    public const string Bootstrap = "bootstrap";
    public const string Conformal = "conformal";
    public const string Bayesian = "bayesian";
    public const string Frequentist = "frequentist";

    public static bool IsValid(string? value)
        => value is Bootstrap or Conformal or Bayesian or Frequentist;
}

public static class EvidenceKinds
{
    public const string DatasetSnapshot = "dataset-snapshot";
    public const string AnalysisRun = "analysis-run";
    public const string ExecutionComparison = "execution-comparison";
    public const string MechanismModel = "mechanism-model";
    public const string KnowledgeSource = "knowledge-source";
    public const string OperatingRegion = "operating-region";
    public const string TransferAssessment = "transfer-assessment";

    public static bool IsValid(string? value)
        => value is DatasetSnapshot or AnalysisRun or ExecutionComparison or
            MechanismModel or KnowledgeSource or OperatingRegion or TransferAssessment;
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
    public string Role { get; init; } = ResearchVariableRoles.Process;
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

public sealed record ResearchProject
{
    public Guid ProjectId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string ProcessName { get; init; }
    public string? ProductName { get; init; }
    public string? MaterialName { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = ResearchProjectStatuses.Draft;
    public IReadOnlyList<ResearchObjective> Objectives { get; init; } = [];
    public IReadOnlyList<ResearchVariable> Variables { get; init; } = [];
    public IReadOnlyList<ResearchConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<ResearchOutcomeConstraint> OutcomeConstraints { get; init; } = [];
    public ResearchOptimizationFeatureSet OptimizationFeatures { get; init; } = new();
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
    public string OwnerUserId { get; init; } = "";
    public IReadOnlyList<string> MemberUserIds { get; init; } = [];
    public string? SiteCode { get; init; }
    public DateTimeOffset? TargetCompletionAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Revision { get; init; }
}

/// <summary>
/// 与一条建议一同冻结的项目定义。后续决定和结果只引用其修订号与内容哈希，
/// 避免项目编辑改变历史证据的解释边界。
/// </summary>
public sealed record ResearchProjectEvidenceSnapshot
{
    public Guid ProjectId { get; init; }
    public int Revision { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string? ProductName { get; init; }
    public string? MaterialName { get; init; }
    public string? SiteCode { get; init; }
    public IReadOnlyList<ResearchVariable> Variables { get; init; } = [];
    public IReadOnlyList<ResearchObjective> Objectives { get; init; } = [];
    public IReadOnlyList<ResearchConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<ResearchOutcomeConstraint> OutcomeConstraints { get; init; } = [];
    public ResearchOptimizationFeatureSet OptimizationFeatures { get; init; } = new();
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
}

public sealed record EvidenceReference
{
    public Guid EvidenceId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Kind { get; init; }
    public required string ReferenceId { get; init; }
    public required string Summary { get; init; }
    public required string ContentHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ResearchHypothesisCausalLink
{
    public required string FromVariableCode { get; init; }
    public required string ToVariableCode { get; init; }
    public required string Mechanism { get; init; }
    public string? Direction { get; init; }
}

public sealed record ResearchHypothesisTemporalFeature
{
    public required string VariableCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public long? DelayMilliseconds { get; init; }
    public long? WindowMilliseconds { get; init; }
}

public sealed record ResearchHypothesisInteraction
{
    public IReadOnlyList<string> VariableCodes { get; init; } = [];
    public required string Description { get; init; }
}

public sealed record ResearchHypothesisFailureCondition
{
    public required string Condition { get; init; }
    public required string ObservableSignal { get; init; }
    public required string RequiredResponse { get; init; }
}

public sealed record ResearchHypothesis
{
    public Guid HypothesisId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Statement { get; init; }
    public required string Rationale { get; init; }
    public string Status { get; init; } = ResearchHypothesisStatuses.Proposed;
    public IReadOnlyList<string> VariableCodes { get; init; } = [];
    public string? ValidationOutcomeCode { get; init; }
    public string? ExpectedEffectDirection { get; init; }
    public double? MinimumEffect { get; init; }
    public IReadOnlyList<string> PossibleConfounders { get; init; } = [];
    public string? Applicability { get; init; }
    public IReadOnlyList<ResearchHypothesisCausalLink> CausalChain { get; init; } = [];
    public IReadOnlyList<ResearchHypothesisTemporalFeature> TemporalFeatures { get; init; } = [];
    public IReadOnlyList<ResearchHypothesisInteraction> Interactions { get; init; } = [];
    public IReadOnlyList<ResearchHypothesisFailureCondition> FailureConditions { get; init; } = [];
    public IReadOnlyList<string> FalsificationConditions { get; init; } = [];
    public IReadOnlyList<EvidenceReference> SupportingEvidence { get; init; } = [];
    public IReadOnlyList<EvidenceReference> OpposingEvidence { get; init; } = [];
    public IReadOnlyList<EvidenceReference> ValidationEvidence { get; init; } = [];
    public double Confidence { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ResearchVariableSetting
{
    public required string VariableCode { get; init; }
    public required double Value { get; init; }
    public required string Unit { get; init; }
}

public sealed record ResearchHypothesisFromExecutionComparisonRequest
{
    public required string BaselineProcessExecutionId { get; init; }
    public IReadOnlyList<string> ProcessExecutionIds { get; init; } = [];
    public int MaximumHypotheses { get; init; } = 3;
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
/// 工程师对日常下一配方建议的不可变回执；不构成设备控制命令。
/// </summary>
public sealed record ResearchRecipeRecommendationDecisionRequest
{
    public required string Decision { get; init; }
    /// <summary>可选的已知实际运行；未知时可在决定冻结后单独关联。</summary>
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ActualExecutionKey { get; init; }
    public IReadOnlyList<ResearchVariableSetting> EngineerSelectedParameters { get; init; } = [];
    public string? Reason { get; init; }
    public string? UsefulnessRating { get; init; }
}

/// <summary>把已冻结的日常建议决定关联到一条真实生产运行。</summary>
public sealed record ResearchRecipeRecommendationExecutionLinkRequest
{
    public required string ActualExecutionKey { get; init; }
}

/// <summary>
/// 从实际工艺执行、参数回读和检验记录冻结的日常建议结果。
/// </summary>
public sealed record ResearchRecipeRecommendationOutcome
{
    public required string ActualExecutionKey { get; init; }
    public int ProjectRevision { get; init; }
    public string ProjectSnapshotHash { get; init; } = "none";
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
    public Guid ProjectId { get; init; }
    public Guid RecommendationId { get; init; }
    public required string RecommendationKey { get; init; }
    public required string Decision { get; init; }
    public int ProjectRevision { get; init; }
    public ResearchProjectEvidenceSnapshot ProjectSnapshot { get; init; } = new();
    public string ProjectSnapshotHash { get; init; } = "none";
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

public sealed record OperatingRegionVariable
{
    public required string VariableCode { get; init; }
    public required double LowerBound { get; init; }
    public required double UpperBound { get; init; }
    public required string Unit { get; init; }
}

public sealed record ResearchOperatingRegion
{
    public Guid OperatingRegionId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = OperatingRegionStatuses.Candidate;
    public IReadOnlyList<OperatingRegionVariable> Variables { get; init; } = [];
    public IReadOnlyList<string> ObjectiveCodes { get; init; } = [];
    public IReadOnlyList<EvidenceReference> Evidence { get; init; } = [];
    public double Confidence { get; init; }
    public required string ConfidenceMethod { get; init; }
    public Guid AnalysisRunId { get; init; }
    public required string AnalysisHash { get; init; }
    public required string Applicability { get; init; }
    public string ValidationLevel { get; init; } = OperatingRegionValidationLevels.Evidence;
    public string? ValidationNotes { get; init; }
    public string? ValidatedBy { get; init; }
    public DateTimeOffset? ValidatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ResearchKnowledgeClaim
{
    public Guid ClaimId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? OperatingRegionId { get; init; }
    public Guid? TransferAssessmentId { get; init; }
    public required string Statement { get; init; }
    public required string Applicability { get; init; }
    public string Status { get; init; } = ResearchKnowledgeStatuses.Draft;
    public IReadOnlyList<EvidenceReference> Evidence { get; init; } = [];
    public string CreatedBy { get; init; } = "";
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ResearchProjectWorkspace
{
    public required ResearchProject Project { get; init; }
    public IReadOnlyList<ResearchHypothesis> Hypotheses { get; init; } = [];
    public IReadOnlyList<ResearchRecipeRecommendation> RecipeRecommendations { get; init; } = [];
    public IReadOnlyList<ResearchRecipeRecommendationDecision> RecipeRecommendationDecisions { get; init; } = [];
    public IReadOnlyList<ResearchRecipeRecommendationFlow> RecipeRecommendationFlows { get; init; } = [];
    public IReadOnlyList<ResearchOperatingRegion> OperatingRegions { get; init; } = [];
    public IReadOnlyList<ResearchKnowledgeClaim> KnowledgeClaims { get; init; } = [];
    public IReadOnlyList<MechanismClaimUsage> MechanismKnowledgeUsages { get; init; } = [];
    public IReadOnlyList<ResearchAuditEntry> Audit { get; init; } = [];
    public IReadOnlyDictionary<string, string> NextCursors { get; init; } =
        new Dictionary<string, string>();
}

public sealed record ResearchPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record ResearchRecipeRecommendationRequest
{
    public int Seed { get; init; }
}

/// <summary>
/// 面向日常生产的下一配方建议。数据来自真实配方运行，不要求用户建立验证计划。
/// </summary>
public sealed record ResearchRecipeRecommendation
{
    public Guid RecommendationId { get; init; }
    public Guid ProjectId { get; init; }
    public int ProjectRevision { get; init; }
    public ResearchProjectEvidenceSnapshot ProjectSnapshot { get; init; } = new();
    public string ProjectSnapshotHash { get; init; } = "none";
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
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ResearchRecipeRecommendationItem
{
    public required string RecommendationKey { get; init; }
    public IReadOnlyList<ResearchVariableSetting> Parameters { get; init; } = [];
    public required OptimizationRunPrediction Prediction { get; init; }
}

public sealed record ResearchAuditEntry
{
    public Guid EntryId { get; init; }
    public Guid ProjectId { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string Action { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public string UserId { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}
