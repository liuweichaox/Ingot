namespace Ingot.Contracts.Inspections;

public sealed record AttachmentUploadResponse
{
    public required Guid AttachmentId { get; init; }
    public required string StorageRef { get; init; }
    public required string Sha256 { get; init; }
    public required string MediaType { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
}

public sealed record InspectionDefinition
{
    public required string Code { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<InspectionCharacteristicDefinition> Characteristics { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record InspectionCharacteristicDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string InputType { get; init; }
    public string? Unit { get; init; }
    public decimal? LowerLimit { get; init; }
    public decimal? UpperLimit { get; init; }
    public bool Required { get; init; } = true;
}

public sealed record PhaseDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int SortOrder { get; init; }
    public bool Required { get; init; } = true;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record PhaseMapping
{
    public required string MappingId { get; init; }
    public required string RecipeId { get; init; }
    public string? RecipeVersion { get; init; }
    public string? RecipeTemplate { get; init; }
    public required string RecipeStep { get; init; }
    public string? RecipeStepName { get; init; }
    public required string PhaseCode { get; init; }
    public string PhaseSource { get; init; } = "recipe";
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record FeatureDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string PhaseCode { get; init; }
    public required string Signal { get; init; }
    public required string Aggregation { get; init; }
    public string? BoundaryMode { get; init; }
    public string? Unit { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset UpdatedAt { get; init; }
}
