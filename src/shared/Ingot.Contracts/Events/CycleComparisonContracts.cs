using System.Text.Json;

namespace Ingot.Contracts.Events;

public sealed record CycleSelectionComparisonRequest
{
    public required string BaselineCycleId { get; init; }

    public IReadOnlyList<string> CycleIds { get; init; } = [];
}

public sealed record CycleComparisonResult
{
    public required string BaselineCycleId { get; init; }

    public required string ProductSeries { get; init; }

    public string? AnalysisPlanId { get; init; }

    public int? AnalysisPlanVersion { get; init; }

    public string? DataModelId { get; init; }

    public int? DataModelVersion { get; init; }

    public string AnalysisScope { get; init; } = "production-cycle";

    public string? AlignmentMode { get; init; }

    /// <summary>
    ///     Historical payloads without this field predate explicit algorithm versioning and are
    ///     interpreted as v1. Current comparison paths always assign their fingerprinted version.
    /// </summary>
    public string FeatureAlgorithmVersion { get; init; } = "stage-relative-v1";

    public string EvidenceLevel { get; init; } = "insufficient";

    public required CycleComparisonRow Baseline { get; init; }

    public IReadOnlyList<CycleComparisonRow> HistoricalCycles { get; init; } = [];

    public IReadOnlyList<CycleSignalComparison> SignalComparisons { get; init; } = [];

    public IReadOnlyList<CycleQualityAssociation> QualityAssociations { get; init; } = [];

    /// <summary>
    ///     将实际配方参数和过程轨迹特征放在同一证据口径下形成的诊断结果。
    ///     候选原因仍是观察性关联，必须经过受控实验才能升级为因果结论。
    /// </summary>
    public CycleDiagnosisSummary Diagnosis { get; init; } = new();

    /// <summary>
    ///     由确定性工具生成的统一调查报告。本地模型只能组织和解释这些字段，
    ///     不能自行补写数值、记录标识或把候选关联升级为根因。
    /// </summary>
    public CycleInvestigationReport Investigation { get; init; } = new();

    public required CycleComparisonAcceptance Acceptance { get; init; }
}

public static class CycleCauseSourceKinds
{
    public const string RecipeParameter = "recipe-parameter";
    public const string ProcessFeature = "process-feature";
}

public static class CycleCauseActionability
{
    public const string Controllable = "controllable";
    public const string Observable = "observable";
}

public sealed record CycleDiagnosisSummary
{
    public string AlgorithmVersion { get; init; } = "robust-stratified-v1";
    public string ModelFamily { get; init; } = "robust-screening-only";
    public string AdjustmentMethod { get; init; } = "none";
    public double? CrossValidationScore { get; init; }
    public int FoldCount { get; init; }
    public int StabilityRuns { get; init; }
    public IReadOnlyList<string> ContextVariables { get; init; } = [];
    public string EvidenceLevel { get; init; } = "insufficient";
    public int PassCycleCount { get; init; }
    public int FailCycleCount { get; init; }
    public double PassEffectiveWeight { get; init; }
    public double FailEffectiveWeight { get; init; }
    public IReadOnlyList<CycleCauseCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<CycleCauseInteraction> Interactions { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed record CycleInvestigationReport
{
    public string Status { get; init; } = "insufficient";
    public string TargetCycleId { get; init; } = "";
    public CycleInvestigationDataQuality DataQuality { get; init; } = new();
    public CycleInvestigationBaseline ComparisonBaseline { get; init; } = new();
    public IReadOnlyList<CycleFirstDeviation> FirstDeviations { get; init; } = [];
    public IReadOnlyList<CycleCauseCandidate> CandidateCauses { get; init; } = [];
    public IReadOnlyList<CycleCounterEvidence> CounterEvidence { get; init; } = [];
    public IReadOnlyList<string> Confounders { get; init; } = [];
    public IReadOnlyList<string> MissingData { get; init; } = [];
    public IReadOnlyList<CycleValidationExperiment> NextExperiments { get; init; } = [];
    public string ConclusionGuardrail { get; init; } =
        "当前结果是观察性候选，必须经过受控重复实验才能升级为已验证原因。";
}

public sealed record CycleInvestigationDataQuality
{
    public string TargetStatus { get; init; } = ProcessDataStatuses.Unavailable;
    public double TargetEvidenceWeight { get; init; }
    public int AvailableComparisonCycles { get; init; }
    public int DegradedComparisonCycles { get; init; }
    public int UnavailableComparisonCycles { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed record CycleInvestigationBaseline
{
    public IReadOnlyList<string> ComparisonCycleIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> MatchingContext { get; init; } =
        new Dictionary<string, string>();
    public int CompleteCycleCount { get; init; }
    public int QualityLinkedCycleCount { get; init; }
    public double EffectiveCycleWeight { get; init; }
}

public sealed record CycleFirstDeviation
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

public sealed record CycleCounterEvidence
{
    public required string CandidateId { get; init; }
    public required string Kind { get; init; }
    public required string Statement { get; init; }
}

public sealed record CycleValidationExperiment
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

public sealed record CycleCauseCandidate
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
    public int PassCycleCount { get; init; }
    public int FailCycleCount { get; init; }
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

public sealed record CycleCauseInteraction
{
    public required string LeftDataSource { get; init; }
    public required string RightDataSource { get; init; }
    public double AdjustedEffect { get; init; }
    public double StabilitySelectionRate { get; init; }
    public double RankScore { get; init; }
}

public sealed record CycleSignalComparison
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

public sealed record CycleQualityAssociation
{
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public string? PhaseName { get; init; }
    public int? PhaseOrder { get; init; }
    public int PassCycleCount { get; init; }
    public int FailCycleCount { get; init; }
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

public sealed record CycleComparisonRow
{
    public required string CorrelationId { get; init; }

    public required string MachineId { get; init; }

    public IReadOnlyList<string> EdgeIds { get; init; } = [];

    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();

    public bool HasStarted { get; init; }

    public bool HasCompleted { get; init; }

    public bool LifecycleComplete { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public double? DurationMs { get; init; }

    public required string ProductSeries { get; init; }

    public string? ProductCode { get; init; }

    public string? RecipeId { get; init; }

    public string? RecipeVersion { get; init; }

    public string? ToolingInstallationId { get; init; }

    public string? ToolingId { get; init; }

    public string? MoldId { get; init; }

    public string? AssemblyRevisionId { get; init; }

    public string? AssemblyRevision { get; init; }

    public string? WorkpieceId { get; init; }

    public string? ExternalBatchRef { get; init; }

    public string? MaterialLotRef { get; init; }

    public int SampleCount { get; init; }

    public int ExpectedSampleCount { get; init; }

    /// <summary>旧接口兼容字段；新分析请使用 ProcessDataQuality。</summary>
    public double? SampleCompleteness { get; init; }

    public ProcessDataQualitySummary ProcessDataQuality { get; init; } = new();

    public double EvidenceWeight { get; init; }

    public int PhaseCount { get; init; }

    public IReadOnlyList<string> InspectionOutcomes { get; init; } = [];

    public string? VisualReviewDecision { get; init; }

    public IReadOnlyList<CycleSignalStatistic> Signals { get; init; } = [];

    public IReadOnlyList<CyclePhaseSummary> Phases { get; init; } = [];

    public CycleAnalysisMaterialization AnalysisMaterialization { get; init; } = new();

    public IReadOnlyList<CycleRecipeParameter> RecipeParameters { get; init; } = [];
}

public sealed record CycleRecipeParameter
{
    public required string Code { get; init; }

    public string? Name { get; init; }

    public string? Unit { get; init; }

    public JsonElement Value { get; init; }
}

public sealed record CycleSignalStatistic
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

    public IReadOnlyList<CycleSignalFeature> Features { get; init; } = [];
}

public sealed record CycleComparisonAcceptance
{
    public int CycleCount { get; init; }

    public int CompleteCycleCount { get; init; }

    public int QualityLinkedCycleCount { get; init; }

    public int VisualReviewCompletedCycleCount { get; init; }

    public int AvailableCycleCount { get; init; }

    public int DegradedCycleCount { get; init; }

    public int UnavailableCycleCount { get; init; }

    public double EffectiveCycleWeight { get; init; }
}
