namespace Ingot.Contracts.ResearchAssets;

public sealed record ResearchAssetPage<T>
{
    public IReadOnlyList<T> Data { get; init; } = [];
    public string? NextCursor { get; init; }
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

public sealed record KnowledgeSource
{
    public Guid SourceId { get; init; }
    /// <summary>知识来源只在所属站点内检索和引用。</summary>
    public string SiteCode { get; init; } = "";
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
