namespace Ingot.Contracts.ProcessImprovement;

public static class ProcessModelStatuses
{
    public const string Draft = "draft";
    public const string Validated = "validated";
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Retired = "retired";

    public static bool IsValid(string? value)
        => value is Draft or Validated or Active or Suspended or Retired;
}

public static class InvestigationStatuses
{
    public const string Open = "open";
    public const string Investigating = "investigating";
    public const string Trialing = "trialing";
    public const string Concluded = "concluded";
    public const string Closed = "closed";

    public static bool IsValid(string? value)
        => value is Open or Investigating or Trialing or Concluded or Closed;
}

public static class PossibleCauseStatuses
{
    public const string Proposed = "proposed";
    public const string Selected = "selected";
    public const string Rejected = "rejected";
    public const string Confirmed = "confirmed";
    public const string Inconclusive = "inconclusive";

    public static bool IsValid(string? value)
        => value is Proposed or Selected or Rejected or Confirmed or Inconclusive;
}

public static class ProcessTrialStatuses
{
    public const string Planned = "planned";
    public const string Approved = "approved";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static bool IsValid(string? value)
        => value is Planned or Approved or Running or Completed or Cancelled;
}

public static class TrialRigorLevels
{
    public const string Exploratory = "exploratory";
    public const string Confirmatory = "confirmatory";

    public static bool IsValid(string? value)
        => value is Exploratory or Confirmatory;
}

public static class ScientificTrialEstimators
{
    public const string WelchDifferenceInMeansCornishFisherV1 =
        "welch-difference-in-means-cornish-fisher-v1";
}

public static class RecommendationStatuses
{
    public const string Draft = "draft";
    public const string Reviewed = "reviewed";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Executed = "executed";
    public const string Verified = "verified";
    public const string RollbackRequired = "rollback-required";
    public const string RolledBack = "rolled-back";
    public const string Withdrawn = "withdrawn";

    public static bool IsValid(string? value)
        => value is Draft or Reviewed or Approved or Rejected or Executed or Verified or
            RollbackRequired or RolledBack or Withdrawn;
}

public static class KnowledgeSourceStatuses
{
    public const string Uploaded = "uploaded";
    public const string Indexed = "indexed";
    public const string Reviewed = "reviewed";
    public const string Retired = "retired";

    public static bool IsValid(string? value)
        => value is Uploaded or Indexed or Reviewed or Retired;
}

/// <summary>
/// Immutable description of the exact records used to train or calibrate a model.
/// </summary>
public sealed record TrainingDatasetVersion
{
    public required string DatasetId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public required string AnalysisPlanId { get; init; }
    public int AnalysisPlanVersion { get; init; } = 1;
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> CycleIds { get; init; } = [];
    public IReadOnlyList<string> FeatureCodes { get; init; } = [];
    public required string TargetCode { get; init; }
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public int RowCount { get; init; }
    public required string ContentHash { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ProcessModelVersion
{
    public required string ModelId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string ModelKind { get; init; } = "quality-risk";
    public required string ProblemCode { get; init; }
    public string Status { get; init; } = ProcessModelStatuses.Draft;
    public required string Algorithm { get; init; }
    public required string DatasetId { get; init; }
    public int DatasetVersion { get; init; } = 1;
    public string? ArtifactRef { get; init; }
    public string? ArtifactSha256 { get; init; }
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> InputFeatureCodes { get; init; } = [];
    public required string OutputCode { get; init; }
    public string UncertaintyMethod { get; init; } = "none";
    public string? ChangeNote { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ModelMetric
{
    public required string Code { get; init; }
    public double Value { get; init; }
    public string? Unit { get; init; }
    public double? RequiredMinimum { get; init; }
    public double? RequiredMaximum { get; init; }
}

public sealed record ModelEvaluation
{
    public Guid EvaluationId { get; init; }
    public required string ModelId { get; init; }
    public int ModelVersion { get; init; } = 1;
    public string Split { get; init; } = "holdout";
    public int SampleCount { get; init; }
    public IReadOnlyList<ModelMetric> Metrics { get; init; } = [];
    public bool Passed { get; init; }
    public string? Notes { get; init; }
    public string EvaluatedBy { get; init; } = "";
    public DateTimeOffset EvaluatedAt { get; init; }
}

public sealed record ModelDriftReading
{
    public Guid ReadingId { get; init; }
    public required string ModelId { get; init; }
    public int ModelVersion { get; init; } = 1;
    public required string MetricCode { get; init; }
    public double Value { get; init; }
    public double WarningThreshold { get; init; }
    public double StopThreshold { get; init; }
    public int SampleCount { get; init; }
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public string RecordedBy { get; init; } = "";
    public DateTimeOffset RecordedAt { get; init; }
}

public sealed record InvestigationCase
{
    public Guid InvestigationId { get; init; }
    public required string Title { get; init; }
    public required string ProblemCode { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = InvestigationStatuses.Open;
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> CycleIds { get; init; } = [];
    public string OwnerUserId { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record PossibleCause
{
    public Guid CauseId { get; init; }
    public Guid InvestigationId { get; init; }
    public required string Title { get; init; }
    public string? ParameterCode { get; init; }
    public string? SignalCode { get; init; }
    public string? PhaseCode { get; init; }
    public string Direction { get; init; } = "unknown";
    public required string Reasoning { get; init; }
    public IReadOnlyList<string> RelatedCycleIds { get; init; } = [];
    public string Status { get; init; } = PossibleCauseStatuses.Proposed;
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TrialParameterChange
{
    public required string ParameterCode { get; init; }
    public string? PhaseCode { get; init; }
    public double BaselineValue { get; init; }
    public double TrialValue { get; init; }
    public required string Unit { get; init; }
    public double AllowedMinimum { get; init; }
    public double AllowedMaximum { get; init; }
}

public sealed record OperatingConstraint
{
    public required string Code { get; init; }
    public required string Description { get; init; }
    public string Operator { get; init; } = "<=";
    public double Limit { get; init; }
    public required string Unit { get; init; }
}

public sealed record TrialMetricDefinition
{
    public required string MetricCode { get; init; }
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public int? PhaseOrder { get; init; }
    public required string Unit { get; init; }
    /// <summary>higher-is-better、lower-is-better 或 two-sided。</summary>
    public string Direction { get; init; } = "two-sided";
}

public sealed record TrialSafetyMetricBinding
{
    public required string ConstraintCode { get; init; }
    public required string SignalCode { get; init; }
    public required string FeatureCode { get; init; }
    public string? PhaseCode { get; init; }
    public int? PhaseOrder { get; init; }
}

public sealed record ExperimentalProtocol
{
    public required string Hypothesis { get; init; }
    public required TrialMetricDefinition PrimaryMetric { get; init; }
    public string AllocationMethod { get; init; } = "blocked";
    public IReadOnlyList<string> BlockingKeys { get; init; } = [];
    public int MinimumControlSampleSize { get; init; } = 10;
    public int MinimumTrialSampleSize { get; init; } = 10;
    public double Alpha { get; init; } = 0.05;
    public string Estimator { get; init; } =
        ScientificTrialEstimators.WelchDifferenceInMeansCornishFisherV1;
    public IReadOnlyList<string> ExclusionRules { get; init; } = [];
    public IReadOnlyList<TrialSafetyMetricBinding> SafetyMetricBindings { get; init; } = [];
    public string PreRegisteredBy { get; init; } = "";
    public DateTimeOffset PreRegisteredAt { get; init; }
}

public sealed record ProcessTrial
{
    public Guid TrialId { get; init; }
    public Guid InvestigationId { get; init; }
    public Guid CauseId { get; init; }
    public required string Name { get; init; }
    public string TrialKind { get; init; } = "controlled-field-trial";
    public string RigorLevel { get; init; } = TrialRigorLevels.Exploratory;
    public ExperimentalProtocol? Protocol { get; init; }
    public string Status { get; init; } = ProcessTrialStatuses.Planned;
    public IReadOnlyList<TrialParameterChange> ParameterChanges { get; init; } = [];
    public IReadOnlyList<OperatingConstraint> SafetyConstraints { get; init; } = [];
    public IReadOnlyList<string> ControlCycleIds { get; init; } = [];
    public IReadOnlyList<string> TrialCycleIds { get; init; } = [];
    public required string StopRule { get; init; }
    public required string RollbackPlan { get; init; }
    public string CreatedBy { get; init; } = "";
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record TrialResult
{
    public Guid ResultId { get; init; }
    public Guid TrialId { get; init; }
    public required string MetricCode { get; init; }
    public double BaselineValue { get; init; }
    public double TrialValue { get; init; }
    public double EffectValue { get; init; }
    public required string Unit { get; init; }
    public double? LowerConfidenceBound { get; init; }
    public double? UpperConfidenceBound { get; init; }
    public int BaselineSampleCount { get; init; }
    public int TrialSampleCount { get; init; }
    public bool SafetyPassed { get; init; }
    public bool CalculatedFromSource { get; init; }
    public string ComputationMethod { get; init; } = "manual";
    public string? EvidenceHash { get; init; }
    public double? StandardError { get; init; }
    public double? DegreesOfFreedom { get; init; }
    public string RecordedBy { get; init; } = "";
    public DateTimeOffset RecordedAt { get; init; }
}

public sealed record InvestigationConclusion
{
    public Guid ConclusionId { get; init; }
    public Guid InvestigationId { get; init; }
    public Guid CauseId { get; init; }
    public Guid TrialId { get; init; }
    public string Decision { get; init; } = PossibleCauseStatuses.Inconclusive;
    public required string Summary { get; init; }
    public IReadOnlyDictionary<string, string> ApplicableContext { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<Guid> ResultIds { get; init; } = [];
    public string ReviewedBy { get; init; } = "";
    public DateTimeOffset ReviewedAt { get; init; }
}

public sealed record KnowledgeSource
{
    public Guid SourceId { get; init; }
    public required string Title { get; init; }
    public string SourceKind { get; init; } = "document";
    public string Status { get; init; } = KnowledgeSourceStatuses.Uploaded;
    public required string StorageRef { get; init; }
    public required string Sha256 { get; init; }
    public required string MediaType { get; init; }
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public string UploadedBy { get; init; } = "";
    public DateTimeOffset UploadedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string ExtractionStatus { get; init; } = "pending";
    public string? ExtractionError { get; init; }
    public string? ExtractorVersion { get; init; }
}

public sealed record KnowledgeCitation
{
    public required string LocationKind { get; init; }
    public int? PageNumber { get; init; }
    public string? SheetName { get; init; }
    public string? CellRange { get; init; }
    public string? Region { get; init; }
    public required string ContentHash { get; init; }
}

public sealed record KnowledgeRecord
{
    public Guid RecordId { get; init; }
    public Guid SourceId { get; init; }
    public string Category { get; init; } = "field-note";
    public string? PageOrSheet { get; init; }
    public string? Region { get; init; }
    public required string Content { get; init; }
    public IReadOnlyDictionary<string, string> StructuredValues { get; init; } = new Dictionary<string, string>();
    public bool HumanReviewed { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string ExtractionMethod { get; init; } = "manual";
    public string ExtractorVersion { get; init; } = "manual-v1";
    public double? ExtractionConfidence { get; init; }
    public KnowledgeCitation? Citation { get; init; }
}

public sealed record RecommendedParameterSetting
{
    public required string ParameterCode { get; init; }
    public string? PhaseCode { get; init; }
    public double CurrentValue { get; init; }
    public double RecommendedValue { get; init; }
    public double AllowedMinimum { get; init; }
    public double AllowedMaximum { get; init; }
    public required string Unit { get; init; }
}

public sealed record ExpectedOutcome
{
    public required string MetricCode { get; init; }
    public double BaselineValue { get; init; }
    public double ExpectedValue { get; init; }
    public required string Unit { get; init; }
    public double? LowerBound { get; init; }
    public double? UpperBound { get; init; }
}

public sealed record RecommendationOutcome
{
    public Guid OutcomeId { get; init; }
    public required string MetricCode { get; init; }
    public double BaselineValue { get; init; }
    public double ActualValue { get; init; }
    public double EffectValue { get; init; }
    public required string Unit { get; init; }
    public int BaselineSampleCount { get; init; }
    public int ActualSampleCount { get; init; }
    public bool SafetyPassed { get; init; }
}

public sealed record RecommendationValueEstimate
{
    public string Currency { get; init; } = "CNY";
    public double ExpectedAnnualValue { get; init; }
    public double TrialCost { get; init; }
    public double ImplementationCost { get; init; }
    public double DownsideAtRisk { get; init; }
    public required string CalculationNote { get; init; }
}

public sealed record RealizedRecommendationValue
{
    public string Currency { get; init; } = "CNY";
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public double GrossValue { get; init; }
    public double ImplementationCost { get; init; }
    public double NetValue { get; init; }
    public required string CalculationNote { get; init; }
}

public sealed record RecommendationVerification
{
    public IReadOnlyList<RecommendationOutcome> Outcomes { get; init; } = [];
    public RealizedRecommendationValue? RealizedValue { get; init; }
    public bool ObjectivesMet { get; init; }
    public bool SafetyPassed { get; init; }
    public string? Notes { get; init; }
    public string VerifiedBy { get; init; } = "";
    public DateTimeOffset VerifiedAt { get; init; }
}

public sealed record ParameterRecommendation
{
    public Guid RecommendationId { get; init; }
    public Guid InvestigationId { get; init; }
    public Guid ConclusionId { get; init; }
    public string? ModelId { get; init; }
    public int? ModelVersion { get; init; }
    public required string Title { get; init; }
    public string Status { get; init; } = RecommendationStatuses.Draft;
    public IReadOnlyDictionary<string, string> ApplicableContext { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<RecommendedParameterSetting> ParameterSettings { get; init; } = [];
    public IReadOnlyList<OperatingConstraint> Constraints { get; init; } = [];
    public IReadOnlyList<ExpectedOutcome> ExpectedOutcomes { get; init; } = [];
    public RecommendationValueEstimate? ValueEstimate { get; init; }
    public required string RiskSummary { get; init; }
    public required string StopRule { get; init; }
    public required string RollbackPlan { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public string? ExecutionReference { get; init; }
    public DateTimeOffset? ExecutedAt { get; init; }
    public RecommendationVerification? Verification { get; init; }
    public string? RollbackExecutionReference { get; init; }
    public DateTimeOffset? RolledBackAt { get; init; }
}

public sealed record ImprovementAuditEntry
{
    public Guid EntryId { get; init; }
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string Action { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    public string UserId { get; init; } = "";
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAt { get; init; }
}
