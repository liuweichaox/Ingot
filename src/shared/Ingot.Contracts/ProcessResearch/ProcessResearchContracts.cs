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
    public const string Rejected = "rejected";
    public const string Inconclusive = "inconclusive";

    public static bool IsValid(string? value)
        => value is Proposed or Selected or Supported or Rejected or Inconclusive;
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

public static class ResearchDesignMethods
{
    public const string EngineerDefined = "engineer-defined";
    public const string FullFactorial = "full-factorial";
    public const string FractionalFactorial = "fractional-factorial";
    public const string ResponseSurface = "response-surface";

    public static bool IsValid(string? value)
        => value is EngineerDefined or FullFactorial or FractionalFactorial or ResponseSurface;
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
    public const string MechanismModel = "mechanism-model";
    public const string KnowledgeSource = "knowledge-source";
    public const string ProcessWindow = "process-window";

    public static bool IsValid(string? value)
        => value is DatasetSnapshot or ExperimentResult or AnalysisRun or
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
    public IReadOnlyList<EvidenceReference> SupportingEvidence { get; init; } = [];
    public IReadOnlyList<EvidenceReference> OpposingEvidence { get; init; } = [];
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

public sealed record ExperimentRunPlan
{
    public required string RunKey { get; init; }
    public int Sequence { get; init; }
    public string? BlockKey { get; init; }
    public string? ReplicateKey { get; init; }
    public IReadOnlyList<ExperimentFactorSetting> Factors { get; init; } = [];
}

public sealed record ResearchExperiment
{
    public Guid ExperimentId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? HypothesisId { get; init; }
    public required string Name { get; init; }
    public string DesignMethod { get; init; } = "engineer-defined";
    public int PlanVersion { get; init; } = 1;
    public int ProjectRevision { get; init; }
    public int RandomizationSeed { get; init; }
    public IReadOnlyList<string> BlockingKeys { get; init; } = [];
    public string Status { get; init; } = ResearchExperimentStatuses.Planned;
    public IReadOnlyList<ExperimentFactorSetting> Factors { get; init; } = [];
    public IReadOnlyList<ExperimentRunPlan> RunPlan { get; init; } = [];
    public IReadOnlyList<string> ObjectiveCodes { get; init; } = [];
    public IReadOnlyList<string> ReplicateKeys { get; init; } = [];
    public IReadOnlyList<Guid> ResultIds { get; init; } = [];
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

public sealed record ResearchExperimentResult
{
    public Guid ResultId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid ExperimentId { get; init; }
    public required string DatasetSnapshotId { get; init; }
    public Guid AnalysisRunId { get; init; }
    public string AnalysisHash { get; init; } = "";
    public IReadOnlyList<ExperimentMetricResult> Metrics { get; init; } = [];
    public int RunCount { get; init; }
    public int ReplicateCount { get; init; }
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
