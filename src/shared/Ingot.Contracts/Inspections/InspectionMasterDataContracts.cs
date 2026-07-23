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
    public IReadOnlyList<string> AllowedValues { get; init; } = [];
    public bool Required { get; init; } = true;
}

public static class InspectionPlanStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retired = "retired";

    public static bool IsValid(string? value)
        => value is Draft or Published or Retired;
}

public sealed record InspectionPlan
{
    public required string PlanId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = InspectionPlanStatuses.Draft;
    public int Priority { get; init; }
    public DateTimeOffset? EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public InspectionPlanScope Scope { get; init; } = new();
    public IReadOnlyList<InspectionPlanItem> Items { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record InspectionPlanScope
{
    public string? ProductSeries { get; init; }
    public string? ProductCode { get; init; }
    public string? RecipeId { get; init; }
    public string? MachineId { get; init; }
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
}

public sealed record InspectionPlanItem
{
    public required string DefinitionCode { get; init; }
    public int DefinitionVersion { get; init; } = 1;
    public int Sequence { get; init; }
    public bool Required { get; init; } = true;
    public bool RequiresAttachment { get; init; }
    public bool RequiresReview { get; init; }
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
    public bool Required { get; init; } = true;
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
    public string? ProductSeries { get; init; }
    public string? ProductCode { get; init; }
    public string? RecipeId { get; init; }
    public string? MachineId { get; init; }
    public bool Enabled { get; init; } = true;
    public bool UseInComparison { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
