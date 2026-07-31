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

public static class ProcessWindowStatuses
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

public static class ProcessWindowValidationLevels
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

public static class ResearchDesignMethods
{
    public const string EngineerDefined = "engineer-defined";
    public const string HistoricalObservation = "historical-observation";
    public const string FullFactorial = "full-factorial";
    public const string FractionalFactorial = "fractional-factorial";
    public const string ResponseSurface = "response-surface";
    public const string BayesianOptimization = "bayesian-optimization";

    public static bool IsValid(string? value)
        => value is EngineerDefined or HistoricalObservation or FullFactorial or FractionalFactorial or ResponseSurface
            or BayesianOptimization;
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
    public const string CycleComparison = "cycle-comparison";
    public const string MechanismModel = "mechanism-model";
    public const string KnowledgeSource = "knowledge-source";
    public const string ProcessWindow = "process-window";

    public static bool IsValid(string? value)
        => value is DatasetSnapshot or ExperimentResult or AnalysisRun or CycleComparison or
            MechanismModel or KnowledgeSource or ProcessWindow;
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
    ///     结果来源。默认使用同名检验特性；也可显式写成
    ///     inspection:&lt;characteristic-code&gt;。
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
///     供受约束贝叶斯优化计算候选配方的安全/质量可行概率。
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

public sealed record ResearchHypothesisFromCycleComparisonRequest
{
    public required string BaselineCycleId { get; init; }
    public IReadOnlyList<string> CycleIds { get; init; } = [];
    public int MaximumHypotheses { get; init; } = 3;
}

/// <summary>
///     将已完成的生产周期作为历史证据纳入研发项目。周期标识同时是实验运行标识，
///     因而不会复制过程、配方或检验数据。
/// </summary>
public sealed record ResearchHistoricalRunImportRequest
{
    public IReadOnlyList<string> CycleIds { get; init; } = [];
}

public sealed record ExperimentRunPlan
{
    public required string RunKey { get; init; }
    public int Sequence { get; init; }
    public string? BlockKey { get; init; }
    public string? ReplicateKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> Factors { get; init; } = [];
}

public sealed record ExperimentExecutionCommand
{
    public Guid CommandId { get; init; }
    public required string RunKey { get; init; }
    public int Sequence { get; init; }
    public string? BlockKey { get; init; }
    public string? ReplicateKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> RequestedFactors { get; init; } = [];
}

/// <summary>
///     设备无关的实验执行交接单。PLC、MES、配方系统或人工操作站只需要消费
///     这组有序命令，并在实际运行中沿用 RunKey；采集侧随后会自动把实际配方、
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
    public required string RunKey { get; init; }
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
    public int FeatureSetVersion { get; init; } = 1;
    public int DerivedFeatureCount { get; init; }
    public string Intent { get; init; } = ResearchOptimizationIntents.ReachSpecification;
    public Guid? HypothesisId { get; init; }
    public int DistinctConditionCount { get; init; }
    public int ReplicatesPerCondition { get; init; } = 1;
    public int BlockCount { get; init; } = 1;
    public IReadOnlyList<OptimizationRunPrediction> RunPredictions { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record ResearchExperiment
{
    public Guid ExperimentId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? HypothesisId { get; init; }
    /// <summary>
    ///     非空时表示该实验是针对指定候选工艺窗口设计的独立验证实验。
    ///     验证实验必须与生成候选窗口的实验分离。
    /// </summary>
    public Guid? ValidationWindowId { get; init; }
    public required string Name { get; init; }
    public string DesignMethod { get; init; } = "engineer-defined";
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
    public IReadOnlyList<string> BaselineRunKeys { get; init; } = [];
    public IReadOnlyList<string> ObjectiveCodes { get; init; } = [];
    public IReadOnlyList<string> ReplicateKeys { get; init; } = [];
    public IReadOnlyList<Guid> ResultIds { get; init; } = [];
    public ResearchOptimizationMetadata? Optimization { get; init; }
    public ResearchExperimentExecution? Execution { get; init; }
    public required string StopRule { get; init; }
    public required string RollbackPlan { get; init; }
    public string CreatedBy { get; init; } = "";
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
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
    public required string RunKey { get; init; }
    /// <summary>
    ///     运行发生时的设备、模具、材料批次、产品和配方等上下文。
    ///     这些字段用于区组、分层、迁移边界和混杂因素判断，不能由计划值替代。
    /// </summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<ExperimentFactorSetting> ActualFactors { get; init; } = [];
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
    public IReadOnlyList<string> ExcludedRunKeys { get; init; } = [];
    public IReadOnlyList<EvidenceReference> Evidence { get; init; } = [];
    public string RecordedBy { get; init; } = "";
    public DateTimeOffset RecordedAt { get; init; }
}

public sealed record ProcessWindowVariable
{
    public required string VariableCode { get; init; }
    public required double LowerBound { get; init; }
    public required double UpperBound { get; init; }
    public required string Unit { get; init; }
}

public sealed record ResearchProcessWindow
{
    public Guid WindowId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = ProcessWindowStatuses.Candidate;
    public IReadOnlyList<ProcessWindowVariable> Variables { get; init; } = [];
    public IReadOnlyList<string> ObjectiveCodes { get; init; } = [];
    public IReadOnlyList<Guid> SupportingExperimentIds { get; init; } = [];
    public IReadOnlyList<Guid> SupportingResultIds { get; init; } = [];
    public IReadOnlyList<EvidenceReference> Evidence { get; init; } = [];
    public double Confidence { get; init; }
    public required string ConfidenceMethod { get; init; }
    public Guid AnalysisRunId { get; init; }
    public required string AnalysisHash { get; init; }
    public required string Applicability { get; init; }
    public string ValidationLevel { get; init; } = ProcessWindowValidationLevels.Evidence;
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
    public Guid? ProcessWindowId { get; init; }
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
    public IReadOnlyList<ResearchExperiment> Experiments { get; init; } = [];
    public IReadOnlyList<ResearchExperimentResult> ExperimentResults { get; init; } = [];
    public IReadOnlyList<ResearchProcessWindow> ProcessWindows { get; init; } = [];
    public IReadOnlyList<ResearchKnowledgeClaim> KnowledgeClaims { get; init; } = [];
    public IReadOnlyList<ResearchAuditEntry> Audit { get; init; } = [];
}

public sealed record ResearchOptimizationRequest
{
    public int BatchSize { get; init; } = 3;
    public int Seed { get; init; }
    public string Intent { get; init; } = ResearchOptimizationIntents.ReachSpecification;
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
