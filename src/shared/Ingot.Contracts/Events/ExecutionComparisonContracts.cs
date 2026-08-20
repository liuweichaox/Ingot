using System.Text.Json;

namespace Ingot.Contracts.Events;

public sealed record ExecutionSelectionComparisonRequest
{
    public required string BaselineProcessExecutionId { get; init; }

    public IReadOnlyList<string> ProcessExecutionIds { get; init; } = [];
    public IReadOnlyList<string> AdditionalKnownUnmeasuredConfounders { get; init; } = [];
}

public sealed record ExecutionComparisonResult
{
    public required string BaselineProcessExecutionId { get; init; }

    public required string ProductFamilyCode { get; init; }

    public string? AnalysisPlanId { get; init; }

    public int? AnalysisPlanVersion { get; init; }

    public string? DataModelId { get; init; }

    public int? DataModelVersion { get; init; }

    public string AnalysisScope { get; init; } = "production-execution";

    public string? AlignmentMode { get; init; }

    /// <summary>生成比较特征的算法版本；尚未计算时为 uncomputed。</summary>
    public string FeatureAlgorithmVersion { get; init; } = "uncomputed";

    public string EvidenceLevel { get; init; } = "insufficient";

    public required ExecutionComparisonRow Baseline { get; init; }

    public IReadOnlyList<ExecutionComparisonRow> HistoricalProcessExecutions { get; init; } = [];

    public IReadOnlyList<ProcessSignalComparison> SignalComparisons { get; init; } = [];

    public IReadOnlyList<ExecutionQualityAssociation> QualityAssociations { get; init; } = [];

    /// <summary>
    ///     将实际控制参数和过程轨迹特征放在同一证据口径下形成的诊断结果。
    ///     候选原因仍是观察性关联，必须经过受控实验才能升级为因果结论。
    /// </summary>
    public ExecutionDiagnosisSummary Diagnosis { get; init; } = new();

    /// <summary>
    ///     由确定性工具生成的统一调查报告。本地模型只能组织和解释这些字段，
    ///     不能自行补写数值、记录标识或把候选关联升级为根因。
    /// </summary>
    public ExecutionInvestigationReport Investigation { get; init; } = new();

    public required ExecutionComparisonAcceptance Acceptance { get; init; }
}

public static class ExecutionCauseSourceKinds
{
    public const string ProcessSpecificationParameter = "control-parameter";
    public const string ProcessFeature = "process-feature";
}

public static class ExecutionCauseActionability
{
    public const string Controllable = "controllable";
    public const string Observable = "observable";
}

public sealed record ExecutionDiagnosisSummary
{
    public string AlgorithmVersion { get; init; } = "robust-stratified-v1";
    public string ModelFamily { get; init; } = "robust-screening-only";
    public string AdjustmentMethod { get; init; } = "none";
    public double? CrossValidationScore { get; init; }
    public int FoldCount { get; init; }
    public int StabilityRuns { get; init; }
    public IReadOnlyList<string> ContextVariables { get; init; } = [];
    public IReadOnlyList<string> AdjustedContextVariables { get; init; } = [];
    public ExecutionAnalysisReadiness Readiness { get; init; } = new();
    public IReadOnlyList<string> ObservedPossibleConfounders { get; init; } = [];
    public IReadOnlyList<ExecutionConfounderDisclosure> KnownUnmeasuredConfounders { get; init; } = [];
    public ExecutionSensitivityAssessment SensitivityAssessment { get; init; } = new();
    public string EvidenceLevel { get; init; } = "insufficient";
    public int PassProcessExecutionCount { get; init; }
    public int FailProcessExecutionCount { get; init; }
    public double PassEffectiveWeight { get; init; }
    public double FailEffectiveWeight { get; init; }
    public IReadOnlyList<ExecutionCauseCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<ExecutionCauseInteraction> Interactions { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed record ExecutionAnalysisReadiness
{
    public string Mode { get; init; } = "descriptive-only";
    public IReadOnlyList<string> BlockingReasons { get; init; } = [];
}

public sealed record ExecutionConfounderDisclosure
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Source { get; init; } = "analysis-plan";
}

public sealed record ExecutionSensitivityAssessment
{
    public string Status { get; init; } = "not-estimable";
    public string Reason { get; init; } =
        "当前模型没有可解释的风险比效应估计和置信区间，不能计算混杂敏感性数值。";
}

public sealed record ExecutionInvestigationReport
{
    public string Status { get; init; } = "insufficient";
    public string TargetProcessExecutionId { get; init; } = "";
    public ExecutionInvestigationDataQuality DataQuality { get; init; } = new();
    public ExecutionInvestigationBaseline ComparisonBaseline { get; init; } = new();
    public IReadOnlyList<ExecutionFirstDeviation> FirstDeviations { get; init; } = [];
    public IReadOnlyList<ExecutionCauseCandidate> CandidateCauses { get; init; } = [];
    public IReadOnlyList<ExecutionCounterEvidence> CounterEvidence { get; init; } = [];
    public IReadOnlyList<string> Confounders { get; init; } = [];
    public IReadOnlyList<string> MissingData { get; init; } = [];
    public IReadOnlyList<ExecutionValidationExperiment> NextExperiments { get; init; } = [];
    public string ConclusionGuardrail { get; init; } =
        "当前结果是观察性候选，必须经过受控重复实验才能升级为已验证原因。";
}

public sealed record ExecutionInvestigationDataQuality
{
    public string TargetStatus { get; init; } = ProcessDataStatuses.Unavailable;
    public double TargetEvidenceWeight { get; init; }
    public int AvailableComparisonProcessExecutions { get; init; }
    public int DegradedComparisonProcessExecutions { get; init; }
    public int UnavailableComparisonProcessExecutions { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed record ExecutionInvestigationBaseline
{
    public IReadOnlyList<string> ComparisonProcessExecutionIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> MatchingContext { get; init; } =
        new Dictionary<string, string>();
    public int CompleteProcessExecutionCount { get; init; }
    public int QualityLinkedProcessExecutionCount { get; init; }
    public double EffectiveProcessExecutionWeight { get; init; }
}

public sealed record ExecutionFirstDeviation
{
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public double? TargetValue { get; init; }
    public double? HistoricalMedian { get; init; }
    public double? RobustDeviation { get; init; }
}

public sealed record ExecutionCounterEvidence
{
    public required string CandidateId { get; init; }
    public required string Kind { get; init; }
    public required string Statement { get; init; }
}

public sealed record ExecutionValidationExperiment
{
    public required string CandidateId { get; init; }
    public required string VariableCode { get; init; }
    public required string DataSource { get; init; }
    public string Design { get; init; } = "two-level-repeated-blocked";
    public int MinimumLevels { get; init; } = 2;
    public int MinimumBlocks { get; init; } = 2;
    public int RepeatsPerCondition { get; init; } = 2;
    public IReadOnlyList<string> BlockingFactors { get; init; } = [];
    public required string Rationale { get; init; }
}

public sealed record ExecutionCauseCandidate
{
    public required string CandidateId { get; init; }
    public required string SourceKind { get; init; }
    public required string Actionability { get; init; }
    public required string VariableCode { get; init; }
    public required string DataSource { get; init; }
    public required string DisplayName { get; init; }
    public string? Unit { get; init; }
    public string? SignalCode { get; init; }
    public string? FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public int PassProcessExecutionCount { get; init; }
    public int FailProcessExecutionCount { get; init; }
    public double PassEffectiveWeight { get; init; }
    public double FailEffectiveWeight { get; init; }
    public double? PassMedian { get; init; }
    public double? FailMedian { get; init; }
    public double? MedianDifference { get; init; }
    public double? RobustEffect { get; init; }
    public double? AdjustedEffect { get; init; }
    public double? ModelImportance { get; init; }
    public double? StabilitySelectionRate { get; init; }
    public double? SignStability { get; init; }
    public double CandidateScore { get; init; }
    public string EvidenceLevel { get; init; } = "insufficient";
    public IReadOnlyList<string> PossibleConfounders { get; init; } = [];
}

public sealed record ExecutionCauseInteraction
{
    public required string LeftDataSource { get; init; }
    public required string RightDataSource { get; init; }
    public double AdjustedEffect { get; init; }
    public double StabilitySelectionRate { get; init; }
    public double RankScore { get; init; }
}

public sealed record ProcessSignalComparison
{
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public double? BaselineValue { get; init; }
    public double? HistoricalMedian { get; init; }
    public double? HistoricalP10 { get; init; }
    public double? HistoricalP90 { get; init; }
    public double? BaselinePercentile { get; init; }
    public double? RobustDeviation { get; init; }
    public double EffectiveWeight { get; init; }
}

public sealed record ExecutionQualityAssociation
{
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public int PassProcessExecutionCount { get; init; }
    public int FailProcessExecutionCount { get; init; }
    public double PassEffectiveWeight { get; init; }
    public double FailEffectiveWeight { get; init; }
    public double? PassMedian { get; init; }
    public double? FailMedian { get; init; }
    public double? MedianDifference { get; init; }
    public double? RobustEffect { get; init; }
    public double CandidateScore { get; init; }
    public string EvidenceLevel { get; init; } = "insufficient";
    public IReadOnlyList<string> PossibleConfounders { get; init; } = [];
}

public sealed record ExecutionComparisonRow
{
    public required string ExecutionId { get; init; }

    public string Kind { get; init; } = ProcessExecutionKinds.Discrete;

    public required string EquipmentId { get; init; }

    public IReadOnlyList<string> EdgeIds { get; init; } = [];

    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();

    public bool HasStarted { get; init; }

    public bool HasCompleted { get; init; }

    public bool LifecycleComplete { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public double? DurationMs { get; init; }

    public required string ProductFamilyCode { get; init; }

    public string? ProductCode { get; init; }

    public string? ProcessSpecificationId { get; init; }

    public string? ProcessSpecificationVersion { get; init; }

    public string? ToolingInstallationId { get; init; }

    public string? ToolingAssemblyId { get; init; }

    public string? AssemblyRevisionId { get; init; }

    public string? AssemblyRevision { get; init; }

    public string? OutputItemId { get; init; }

    public string? ExternalBatchRef { get; init; }

    public string? MaterialLotRef { get; init; }

    public int SampleCount { get; init; }

    public int ExpectedSampleCount { get; init; }

    public ProcessDataQualitySummary ProcessDataQuality { get; init; } = new();

    public double EvidenceWeight { get; init; }

    public int PhaseCount { get; init; }

    public IReadOnlyList<string> InspectionOutcomes { get; init; } = [];

    public string? VisualReviewDecision { get; init; }

    public IReadOnlyList<ProcessSignalStatistic> Signals { get; init; } = [];

    public IReadOnlyList<ProcessPhaseSummary> Phases { get; init; } = [];

    public ProcessExecutionAnalysisMaterialization AnalysisMaterialization { get; init; } = new();

    public IReadOnlyList<ExecutionControlParameterValue> ControlParameters { get; init; } = [];
}

public sealed record ExecutionControlParameterValue
{
    public required string Code { get; init; }

    public string? Name { get; init; }

    public string? Unit { get; init; }

    /// <summary>参数事实的来源，例如 PLC 回读或 MES 批次关联的配方快照。</summary>
    public string? Source { get; init; }

    /// <summary>来源侧的捕获状态；用于区分直接回读、源记录关联和推断值。</summary>
    public string? CaptureStatus { get; init; }

    public JsonElement Value { get; init; }
}

public sealed record ProcessSignalStatistic
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public string? Unit { get; init; }

    public int SampleCount { get; init; }

    public double? Average { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double ValidDurationMs { get; init; }

    public double Coverage { get; init; }

    public IReadOnlyList<ProcessSignalFeature> Features { get; init; } = [];
}

public sealed record ExecutionComparisonAcceptance
{
    public int ProcessExecutionCount { get; init; }

    public int CompleteProcessExecutionCount { get; init; }

    public int QualityLinkedProcessExecutionCount { get; init; }

    public int VisualReviewCompletedProcessExecutionCount { get; init; }

    public int AvailableProcessExecutionCount { get; init; }

    public int DegradedProcessExecutionCount { get; init; }

    public int UnavailableProcessExecutionCount { get; init; }

    public double EffectiveProcessExecutionWeight { get; init; }
}
