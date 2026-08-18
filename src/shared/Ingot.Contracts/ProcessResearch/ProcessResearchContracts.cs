using System.Text.Json;
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Contracts.ProcessResearch;

public static class ResearchProjectStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Validating = "validating";
    public const string Completed = "completed";
    public const string Archived = "archived";

    public static bool IsValid(string? value)
        => value is Draft or Active or Validating or Completed or Archived;
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

public static class ResearchExperimentStatuses
{
    public const string Planned = "planned";
    public const string Approved = "approved";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static bool IsValid(string? value)
        => value is Planned or Approved or Running or Completed or Cancelled;
}

public static class ResearchExperimentExecutionCategories
{
    public const string Offline = "offline";
    public const string Shadow = "shadow";
    public const string ControlledOnline = "controlled-online";

    public static bool IsValid(string? value)
        => value is Offline or Shadow or ControlledOnline;
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

public static class ResearchExperimentExecutionStates
{
    public const string AwaitingApproval = "awaiting-approval";
    public const string Ready = "ready";
    public const string Dispatched = "dispatched";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static bool IsValid(string? value)
        => value is AwaitingApproval or Ready or Dispatched or Completed or Cancelled;
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
    public const string ValidateHypothesis = "validate-hypothesis";

    public static bool IsValid(string? value)
        => value is ReachSpecification or ValidateHypothesis;
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

public static class ResearchOptimizationModes
{
    public const string Experiment = "experiment";
    public const string Shadow = "shadow";
    public const string Controlled = "controlled";

    public static bool IsValid(string? value)
        => value is Experiment or Shadow or Controlled;
}

public static class ResearchControlledDecisionStatuses
{
    public const string Accepted = "accepted";
    public const string Modified = "modified";
    public const string Rejected = "rejected";

    public static bool IsValid(string? value)
        => value is Accepted or Modified or Rejected;
}

public static class ResearchShadowDecisionStatuses
{
    public const string Accepted = "accepted";
    public const string Modified = "modified";
    public const string Rejected = "rejected";

    public static bool IsValid(string? value)
        => value is Accepted or Modified or Rejected;
}

public static class ResearchDesignMethods
{
    public const string EngineerDefined = "engineer-defined";
    public const string HistoricalObservation = "historical-observation";
    public const string FullFactorial = "full-factorial";
    public const string FractionalFactorial = "fractional-factorial";
    public const string ResponseSurface = "response-surface";
    public const string LatinHypercube = "latin-hypercube";
    public const string BayesianOptimization = "bayesian-optimization";

    public static bool IsValid(string? value)
        => value is EngineerDefined or HistoricalObservation or FullFactorial or FractionalFactorial or ResponseSurface
            or LatinHypercube or BayesianOptimization;
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
    public const string ExperimentResult = "experiment-result";
    public const string AnalysisRun = "analysis-run";
    public const string ExecutionComparison = "execution-comparison";
    public const string MechanismModel = "mechanism-model";
    public const string KnowledgeSource = "knowledge-source";
    public const string OperatingRegion = "operating-region";
    public const string TransferAssessment = "transfer-assessment";

    public static bool IsValid(string? value)
        => value is DatasetSnapshot or ExperimentResult or AnalysisRun or ExecutionComparison or
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
    /// <summary>
    ///     结果来源。默认使用同名检验特性；可显式写成
    ///     inspection:&lt;characteristic-code&gt;，或用
    ///     inspection-outcome:&lt;definition-code&gt; 将 PASS/FAIL 映射为 1/0。
    /// </summary>
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

/// <summary>
///     由实测结果定义的可行性边界。它与控制参数硬边界分开建模，
///     供受约束贝叶斯优化计算候选工艺规范的安全/质量可行概率。
/// </summary>
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

/// <summary>
///     安全的声明式候选特征。输入只能引用项目可控变量或排在此前的派生特征；
///     优化服务不执行任意表达式，也不根据变量名称猜测工艺含义。
/// </summary>
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
    public IReadOnlyList<ResearchExperimentSafetyTemplate> SafetyTemplates { get; init; } = [];
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

public sealed record ResearchExperimentSafetyTemplate
{
    public required string ExecutionCategory { get; init; }
    public required string StopRule { get; init; }
    public required string RollbackPlan { get; init; }
    public string? Name { get; init; }
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

public sealed record ExperimentFactorSetting
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

/// <summary>
///     将已完成的生产过程执行作为历史证据纳入研发项目。过程执行标识同时是实验运行标识，
///     因而不会复制过程、工艺规范或检验数据。
/// </summary>
public sealed record ResearchHistoricalRunImportRequest
{
    public IReadOnlyList<string> ProcessExecutionIds { get; init; } = [];
}

public sealed record ExperimentRunPlan
{
    public required string ExecutionKey { get; init; }
    public int Sequence { get; init; }
    public string? BlockKey { get; init; }
    public string? ReplicateKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> Factors { get; init; } = [];
}

/// <summary>
///     经典 DOE 的无状态预览请求。预览只生成可编辑的运行计划，不会保存实验、
///     改变审批状态或向设备下发任何命令。
/// </summary>
public sealed record ResearchExperimentDesignRequest
{
    public required string DesignMethod { get; init; }
    public IReadOnlyList<string> VariableCodes { get; init; } = [];
    public int Levels { get; init; } = 2;
    public int ReplicatesPerCondition { get; init; } = 1;
    public int BlockCount { get; init; } = 1;
    public int SampleCount { get; init; }
    public string? ResponseSurfaceFamily { get; init; }
    public int RandomizationSeed { get; init; }
}

public sealed record ResearchExperimentDesignPreview
{
    public required string DesignMethod { get; init; }
    public int RandomizationSeed { get; init; }
    public IReadOnlyList<ExperimentRunPlan> RunPlan { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? AliasStructure { get; init; }
    public string? ResponseSurfaceFamily { get; init; }
}

public sealed record ResearchExperimentValidationIssue
{
    public required string Field { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? FixHint { get; init; }
}

public sealed record ResearchExperimentValidationResult
{
    public IReadOnlyList<ResearchExperimentValidationIssue> Errors { get; init; } = [];
    public bool IsValid => Errors.Count == 0;
}

public sealed record ExperimentExecutionCommand
{
    public Guid CommandId { get; init; }
    public required string ExecutionKey { get; init; }
    public int Sequence { get; init; }
    public string? BlockKey { get; init; }
    public string? ReplicateKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> RequestedFactors { get; init; } = [];
}

/// <summary>
///     设备无关的实验执行交接单。PLC、MES、工艺规范系统或人工操作站只需要消费
///     这组有序命令，并在实际运行中沿用 ExecutionKey；采集侧随后会自动把实际工艺规范、
///     过程轨迹和检验结果关联回同一运行。
/// </summary>
public sealed record ResearchExperimentExecution
{
    public Guid DispatchId { get; init; }
    public string Mode { get; init; } = "operator-confirmed";
    public string State { get; init; } = ResearchExperimentExecutionStates.AwaitingApproval;
    public IReadOnlyList<ExperimentExecutionCommand> Commands { get; init; } = [];
    public string? DispatchedBy { get; init; }
    public DateTimeOffset? DispatchedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
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

public sealed record ResearchOptimizationMetadata
{
    public required string ModelVersion { get; init; }
    public required string InputHash { get; init; }
    public int ObservationCount { get; init; }
    public int AutoAssembledObservationCount { get; init; }
    public int PendingExperimentCount { get; init; }
    public int ProcessFeatureCount { get; init; }
    public string FeatureSetId { get; init; } = "generic";
    public string MechanismKnowledgeSnapshotHash { get; init; } = "none";
    public int FeatureSetVersion { get; init; } = 1;
    public int DerivedFeatureCount { get; init; }
    public string Intent { get; init; } = ResearchOptimizationIntents.ReachSpecification;
    public string Mode { get; init; } = ResearchOptimizationModes.Experiment;
    public Guid? HypothesisId { get; init; }
    public int DistinctConditionCount { get; init; }
    public int ReplicatesPerCondition { get; init; } = 1;
    public int BlockCount { get; init; } = 1;
    public IReadOnlyList<OptimizationRunPrediction> RunPredictions { get; init; } = [];
    public ResearchOnlineAdmissionEvidence? OnlineAdmission { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
///     工程师在不知道该次运行结果时，对一条旁路模型建议作出的预注册选择。
///     模型建议本身由服务端从冻结的优化实验快照复制，调用方不能覆盖。
/// </summary>
public sealed record ResearchShadowDecisionRequest
{
    public required string Decision { get; init; }
    public required string ActualExecutionKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> EngineerSelectedFactors { get; init; } = [];
    public string? RejectionReason { get; init; }
    public IReadOnlyList<string> SiteLimitations { get; init; } = [];
    public IReadOnlyDictionary<string, string> ContextSnapshot { get; init; } =
        new Dictionary<string, string>();
}

public sealed record ResearchShadowOutcome
{
    public required string ActualExecutionKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> ActualFactors { get; init; } = [];
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

public static class ResearchApplicabilityStatuses
{
    public const string InDomain = "in-domain";
    public const string ParameterExtrapolation = "parameter-extrapolation";
    public const string ContextShift = "context-shift";
    public const string InsufficientHistory = "insufficient-history";
}

public sealed record ResearchShadowApplicabilityAssessment
{
    public required string Status { get; init; }
    public int HistoricalObservationCount { get; init; }
    public double? NearestNormalizedParameterDistance { get; init; }
    public IReadOnlyList<string> ParameterExtrapolations { get; init; } = [];
    public IReadOnlyList<string> UnseenContextValues { get; init; } = [];
    public required string Summary { get; init; }
}

/// <summary>
///     影子建议不下发设备。它把冻结的模型输入、建议、人工选择和后续源数据结果
///     绑定到一起，用于评估采用率、未建模现场限制、校准和设置偏差。
/// </summary>
public sealed record ResearchShadowRecommendation
{
    public Guid RecommendationId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid ExperimentId { get; init; }
    public required string SuggestionExecutionKey { get; init; }
    public required string ActualExecutionKey { get; init; }
    public required string Decision { get; init; }
    public required string ModelVersion { get; init; }
    public required string ModelInputHash { get; init; }
    public int ProjectRevision { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> SuggestedFactors { get; init; } = [];
    public IReadOnlyList<ExperimentFactorSetting> EngineerSelectedFactors { get; init; } = [];
    public required OptimizationRunPrediction Prediction { get; init; }
    public required ResearchShadowApplicabilityAssessment Applicability { get; init; }
    public string? RejectionReason { get; init; }
    public IReadOnlyList<string> SiteLimitations { get; init; } = [];
    public IReadOnlyDictionary<string, string> ContextSnapshot { get; init; } =
        new Dictionary<string, string>();
    public required string DecisionSnapshotHash { get; init; }
    public string DecidedBy { get; init; } = "";
    public DateTimeOffset DecidedAt { get; init; }
    public ResearchShadowOutcome? Outcome { get; init; }
}

public sealed record ResearchShadowCalibrationMetric
{
    public required string ObjectiveCode { get; init; }
    public int CheckedCount { get; init; }
    public int CoveredCount { get; init; }
    public double? CoverageRate { get; init; }
}

public sealed record ResearchShadowSafetyEvent
{
    public Guid RecommendationId { get; init; }
    public required string ActualExecutionKey { get; init; }
    public required string ConstraintCode { get; init; }
    public double ObservedValue { get; init; }
    public required string Operator { get; init; }
    public double Limit { get; init; }
    public required string Unit { get; init; }
}

public sealed record ResearchShadowStopSignal
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Reason { get; init; }
}

public sealed record ResearchShadowCampaignReport
{
    public Guid ProjectId { get; init; }
    /// <summary>Missing in historical payloads means the thresholds predated policy versioning.</summary>
    public string ValidationPolicyVersion { get; init; } = "not-evaluated";
    public string MechanismKnowledgeSnapshotHash { get; init; } = "none";
    public int TotalRecommendations { get; init; }
    public int AcceptedCount { get; init; }
    public int ModifiedCount { get; init; }
    public int RejectedCount { get; init; }
    public double? AdoptionRate { get; init; }
    public int CompletedOutcomeCount { get; init; }
    public int InvalidOutcomeCount { get; init; }
    public int ContextShiftCount { get; init; }
    public int ParameterExtrapolationCount { get; init; }
    public int SettingDeviationCount { get; init; }
    public IReadOnlyList<ResearchShadowCalibrationMetric> Calibration { get; init; } = [];
    public IReadOnlyList<ResearchShadowSafetyEvent> SafetyEvents { get; init; } = [];
    public IReadOnlyList<string> RejectionReasons { get; init; } = [];
    public IReadOnlyList<ResearchShadowStopSignal> StopSignals { get; init; } = [];
    public bool StopRecommended { get; init; }
    public required string ReportHash { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public static class ResearchHistoricalReplayStatuses
{
    public const string Generated = "generated";
    public const string Reviewed = "reviewed";
}

public static class ResearchRollbackDrillStatuses
{
    public const string Recorded = "recorded";
    public const string Reviewed = "reviewed";
}

public sealed record ResearchRollbackDrillRequest
{
    public required string Name { get; init; }
    public required string Scenario { get; init; }
    public required string StopTrigger { get; init; }
    public required string RollbackTarget { get; init; }
    public IReadOnlyList<string> ExpectedActions { get; init; } = [];
    public IReadOnlyList<string> ObservedActions { get; init; } = [];
    public bool Passed { get; init; }
    public required string EvidenceReference { get; init; }
    public required string EvidenceContentHash { get; init; }
    public DateTimeOffset ConductedAt { get; init; }
}

/// <summary>
///     受控在线前的停止与回退演练证据。演练人提交后内容不可改，且必须由另一名
///     工程师复核；在线门禁只接受复核通过且本身 Passed=true 的记录。
/// </summary>
public sealed record ResearchRollbackDrill
{
    public Guid DrillId { get; init; }
    public Guid ProjectId { get; init; }
    public int ProjectRevision { get; init; }
    public string Status { get; init; } = ResearchRollbackDrillStatuses.Recorded;
    public required string Name { get; init; }
    public required string Scenario { get; init; }
    public required string StopTrigger { get; init; }
    public required string RollbackTarget { get; init; }
    public IReadOnlyList<string> ExpectedActions { get; init; } = [];
    public IReadOnlyList<string> ObservedActions { get; init; } = [];
    public bool Passed { get; init; }
    public required string EvidenceReference { get; init; }
    public required string EvidenceContentHash { get; init; }
    public required string RecordHash { get; init; }
    public string ConductedBy { get; init; } = "";
    public DateTimeOffset ConductedAt { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
}

public sealed record ResearchHistoricalReplayRequest
{
    public int SeedCount { get; init; } = 30;
    public int? Budget { get; init; }
    public int InitialObservationCount { get; init; } = 3;
}

public sealed record ResearchReplayMethodSummary
{
    public double SuccessRate { get; init; }
    public double? MedianTrials { get; init; }
    public double? MeanTrials { get; init; }
    public int Runs { get; init; }
}

public sealed record ResearchHistoricalReplayReport
{
    public Guid ReportId { get; init; }
    public Guid ProjectId { get; init; }
    /// <summary>Missing in historical payloads means the thresholds predated policy versioning.</summary>
    public string ValidationPolicyVersion { get; init; } = "not-evaluated";
    public string MechanismKnowledgeSnapshotHash { get; init; } = "none";
    public string Status { get; init; } = ResearchHistoricalReplayStatuses.Generated;
    public required string DatasetSnapshotHash { get; init; }
    public int UniqueConditionCount { get; init; }
    public int SourceRunCount { get; init; }
    public int Budget { get; init; }
    public int SeedCount { get; init; }
    public int InitialObservationCount { get; init; }
    public int? OriginalOrderTrials { get; init; }
    public required ResearchReplayMethodSummary Optimizer { get; init; }
    public required ResearchReplayMethodSummary Random { get; init; }
    public ResearchReplayMethodSummary? ResponseSurface { get; init; }
    public IReadOnlyList<string> BaselineMethods { get; init; } = [];
    /// <summary>Historical payloads without this value predate preregistered baselines.</summary>
    public string PreregistrationHash { get; init; } = "not-registered";
    public double? PredictionIntervalCoverage { get; init; }
    public int PredictionIntervalChecks { get; init; }
    public int OptimizerSafetyViolationCount { get; init; }
    public required string EnginePolicy { get; init; }
    public required string EvidenceKind { get; init; }
    public required string Limitations { get; init; }
    public bool GatePassed { get; init; }
    public IReadOnlyList<string> GateFailures { get; init; } = [];
    public JsonElement RawResult { get; init; }
    public required string ReportHash { get; init; }
    public string GeneratedBy { get; init; } = "";
    public DateTimeOffset GeneratedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
}

/// <summary>
///     进入受控在线建议前冻结的准入证据。它只证明当时允许提出一条候选建议，
///     不替代工程师逐条确认，也不授权 Platform 直接写设备。
/// </summary>
public sealed record ResearchOnlineAdmissionEvidence
{
    /// <summary>Missing in historical payloads means the thresholds predated policy versioning.</summary>
    public string ValidationPolicyVersion { get; init; } = "not-evaluated";
    public string MechanismKnowledgeSnapshotHash { get; init; } = "none";
    public bool Eligible { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public Guid? HistoricalReplayReportId { get; init; }
    public string? HistoricalReplayReportHash { get; init; }
    public string? ShadowReportHash { get; init; }
    public Guid? RollbackDrillId { get; init; }
    public string? RollbackDrillRecordHash { get; init; }
    public int ValidShadowOutcomeCount { get; init; }
    public int ShadowRecommendationCount { get; init; }
    public DateTimeOffset AssessedAt { get; init; }
}

public sealed record ResearchOnlineResidualComparison
{
    public required string ObjectiveCode { get; init; }
    public int ShadowCount { get; init; }
    public int OnlineCount { get; init; }
    public double? ShadowMeanResidual { get; init; }
    public double? OnlineMeanResidual { get; init; }
    public double? MeanResidualShift { get; init; }
    public double? ShiftLower95 { get; init; }
    public double? ShiftUpper95 { get; init; }
    public bool SystematicShiftDetected { get; init; }
}

public sealed record ResearchOnlineCampaignReport
{
    public Guid ProjectId { get; init; }
    public int TotalSuggestions { get; init; }
    public int AcceptedCount { get; init; }
    public int ModifiedCount { get; init; }
    public int RejectedCount { get; init; }
    public int RunningCount { get; init; }
    public int CompletedResultCount { get; init; }
    public int ValidOutcomeCount { get; init; }
    public int SettingDeviationCount { get; init; }
    public int SafetyViolationCount { get; init; }
    public IReadOnlyList<ResearchShadowCalibrationMetric> Calibration { get; init; } = [];
    public IReadOnlyList<ResearchOnlineResidualComparison> ShadowComparisons { get; init; } = [];
    public IReadOnlyList<ResearchShadowStopSignal> StopSignals { get; init; } = [];
    public bool StopRecommended { get; init; }
    public required string ReportHash { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ResearchControlledDecisionRequest
{
    public required string Decision { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> ApprovedFactors { get; init; } = [];
    public string? Reason { get; init; }
}

/// <summary>
///     单条受控建议的人工决策快照。SuggestedFactors 永远保留模型原建议；
///     ApprovedFactors 是工程师接受或修改后允许进入执行交接单的设置。
/// </summary>
public sealed record ResearchControlledDecision
{
    public required string Decision { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> SuggestedFactors { get; init; } = [];
    public IReadOnlyList<ExperimentFactorSetting> ApprovedFactors { get; init; } = [];
    public string? Reason { get; init; }
    public required string DecisionSnapshotHash { get; init; }
    public string DecidedBy { get; init; } = "";
    public DateTimeOffset DecidedAt { get; init; }
}

public sealed record ResearchExperiment
{
    public Guid ExperimentId { get; init; }
    public Guid ProjectId { get; init; }
    /// <summary>
    ///     实验自身的乐观并发版本。新实验从 1 开始，每次持久化变更递增；
    ///     与 ProjectRevision（实验设计所依据的项目定义版本）含义不同。
    /// </summary>
    public int Revision { get; init; } = 1;
    public Guid? HypothesisId { get; init; }
    /// <summary>
    ///     非空时表示该实验是针对指定候选工艺操作域设计的独立验证实验。
    ///     验证实验必须与生成候选操作域的实验分离。
    /// </summary>
    public Guid? ValidationOperatingRegionId { get; init; }
    public required string Name { get; init; }
    public string DesignMethod { get; init; } = "engineer-defined";
    public string ExecutionCategory { get; init; } = ResearchExperimentExecutionCategories.Offline;
    public string? SafetyTemplateSource { get; init; }
    public int PlanVersion { get; init; } = 1;
    public int ProjectRevision { get; init; }
    public int RandomizationSeed { get; init; }
    public IReadOnlyList<string> BlockingKeys { get; init; } = [];
    public string Status { get; init; } = ResearchExperimentStatuses.Planned;
    public IReadOnlyList<ExperimentFactorSetting> Factors { get; init; } = [];
    public IReadOnlyList<ExperimentRunPlan> RunPlan { get; init; } = [];
    /// <summary>
    ///     明确作为对照组的运行标识。可以引用本实验中的对照运行，或当前项目中
    ///     已导入的历史运行/已完成实验运行；未声明时不得从项目历史中自动拼接对照。
    /// </summary>
    public IReadOnlyList<string> BaselineExecutionKeys { get; init; } = [];
    public IReadOnlyList<string> ObjectiveCodes { get; init; } = [];
    public IReadOnlyList<string> ReplicateKeys { get; init; } = [];
    public IReadOnlyList<Guid> ResultIds { get; init; } = [];
    public ResearchOptimizationMetadata? Optimization { get; init; }
    public ResearchControlledDecision? ControlledDecision { get; init; }
    public ResearchExperimentExecution? Execution { get; init; }
    public required string StopRule { get; init; }
    public required string RollbackPlan { get; init; }
    public string CreatedBy { get; init; } = "";
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ResearchExperimentCloneRequest
{
    public string? Name { get; init; }
}

public sealed record ExperimentMetricResult
{
    public required string ObjectiveCode { get; init; }
    public double BaselineValue { get; init; }
    public double ObservedValue { get; init; }
    public double EffectValue { get; init; }
    public double? LowerConfidenceBound { get; init; }
    public double? UpperConfidenceBound { get; init; }
    public required string Unit { get; init; }
    public int BaselineSampleCount { get; init; }
    public int ExperimentSampleCount { get; init; }
    public required string ComputationMethod { get; init; }
}

public sealed record ExperimentRunObservation
{
    public required string ExecutionKey { get; init; }
    /// <summary>
    ///     运行发生时的设备、工装总成、材料批次、产品和工艺规范等上下文。
    ///     这些字段用于区组、分层、迁移边界和混杂因素判断，不能由计划值替代。
    /// </summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<ExperimentFactorSetting> ActualFactors { get; init; } = [];
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

public sealed record ResearchExperimentResult
{
    public Guid ResultId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid ExperimentId { get; init; }
    public required string DatasetSnapshotId { get; init; }
    public Guid AnalysisRunId { get; init; }
    public string AnalysisHash { get; init; } = "";
    public IReadOnlyList<ExperimentMetricResult> Metrics { get; init; } = [];
    public IReadOnlyList<ExperimentRunObservation> RunObservations { get; init; } = [];
    public int RunCount { get; init; }
    public int ReplicateCount { get; init; }
    public int DistinctBlockCount { get; init; }
    public int DistinctMaterialLotCount { get; init; }
    public int DistinctEquipmentCount { get; init; }
    public bool SafetyPassed { get; init; }
    public bool CalculatedFromSource { get; init; }
    public IReadOnlyList<string> ExcludedExecutionKeys { get; init; } = [];
    public IReadOnlyList<EvidenceReference> Evidence { get; init; } = [];
    public string RecordedBy { get; init; } = "";
    public DateTimeOffset RecordedAt { get; init; }
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
    public IReadOnlyList<Guid> SupportingExperimentIds { get; init; } = [];
    public IReadOnlyList<Guid> SupportingResultIds { get; init; } = [];
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

public sealed record ResearchTransferAssessmentRequest
{
    public Guid SourceOperatingRegionId { get; init; }
    public Guid TransferResultId { get; init; }
    public Guid ColdStartResultId { get; init; }
    public string? Notes { get; init; }
}

public sealed record ResearchTransferContextDifference
{
    public required string Field { get; init; }
    public string? SourceValue { get; init; }
    public string? TargetValue { get; init; }
}

/// <summary>
///     将一个已发布工艺操作域在目标项目上的实测结果，与目标项目从零建立的对照结果比较。
///     记录冻结源/目标版本、结果哈希和计算结果；Beneficial 仅表示本次有收益，不能代替重复验证。
/// </summary>
public sealed record ResearchTransferAssessment
{
    public Guid AssessmentId { get; init; }
    public Guid ProjectId { get; init; }
    public int TargetProjectRevision { get; init; }
    public Guid SourceProjectId { get; init; }
    public int SourceProjectRevision { get; init; }
    public Guid SourceOperatingRegionId { get; init; }
    public required string SourceOperatingRegionAnalysisHash { get; init; }
    public Guid TransferResultId { get; init; }
    public required string TransferResultAnalysisHash { get; init; }
    public Guid ColdStartResultId { get; init; }
    public required string ColdStartResultAnalysisHash { get; init; }
    public string Status { get; init; } = ResearchTransferAssessmentStatuses.Recorded;
    public required string Outcome { get; init; }
    public bool SchemaCompatible { get; init; }
    public bool EvidenceSufficient { get; init; }
    public bool SafetyPassed { get; init; }
    public bool NegativeTransferDetected { get; init; }
    public double? TransferNormalizedLoss { get; init; }
    public double? ColdStartNormalizedLoss { get; init; }
    public double? RelativeGain { get; init; }
    public IReadOnlyList<ResearchTransferContextDifference> ContextDifferences { get; init; } = [];
    public IReadOnlyList<string> Failures { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? Notes { get; init; }
    public required string RecordHash { get; init; }
    public string CreatedBy { get; init; } = "";
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ResearchProjectWorkspace
{
    public required ResearchProject Project { get; init; }
    public IReadOnlyList<ResearchHypothesis> Hypotheses { get; init; } = [];
    public IReadOnlyList<ResearchExperiment> Experiments { get; init; } = [];
    public IReadOnlyList<ResearchExperimentResult> ExperimentResults { get; init; } = [];
    public IReadOnlyList<ResearchShadowRecommendation> ShadowRecommendations { get; init; } = [];
    public ResearchShadowCampaignReport? ShadowReport { get; init; }
    public IReadOnlyList<ResearchHistoricalReplayReport> HistoricalReplayReports { get; init; } = [];
    public IReadOnlyList<ResearchRollbackDrill> RollbackDrills { get; init; } = [];
    public ResearchOnlineCampaignReport? OnlineReport { get; init; }
    public IReadOnlyList<ResearchOperatingRegion> OperatingRegions { get; init; } = [];
    public IReadOnlyList<ResearchKnowledgeClaim> KnowledgeClaims { get; init; } = [];
    public IReadOnlyList<MechanismClaimUsage> MechanismKnowledgeUsages { get; init; } = [];
    public IReadOnlyList<ResearchTransferAssessment> TransferAssessments { get; init; } = [];
    public IReadOnlyList<ResearchAuditEntry> Audit { get; init; } = [];
}

public sealed record ResearchOptimizationRequest
{
    public int BatchSize { get; init; } = 3;
    public int Seed { get; init; }
    public string Intent { get; init; } = ResearchOptimizationIntents.ReachSpecification;
    public string Mode { get; init; } = ResearchOptimizationModes.Experiment;
    public Guid? HypothesisId { get; init; }
    public bool AutoAssembleObservations { get; init; } = true;
    public int ReplicatesPerCondition { get; init; } = 1;
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
