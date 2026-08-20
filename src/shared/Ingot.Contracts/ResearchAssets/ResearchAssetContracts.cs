namespace Ingot.Contracts.ResearchAssets;

public sealed record ResearchAssetPage<T>
{
    public IReadOnlyList<T> Data { get; init; } = [];
    public string? NextCursor { get; init; }
}

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
///     用于训练或校准模型的精确记录集合的不可变说明。
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
    public IReadOnlyList<string> ProcessExecutionIds { get; init; } = [];
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

public sealed record ResearchAssetAuditEntry
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
