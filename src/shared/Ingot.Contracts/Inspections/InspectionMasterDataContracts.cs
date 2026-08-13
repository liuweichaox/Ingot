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
    /// <summary>
    ///     非数值特性的合格值。服务端依据此集合判定 PASS；未配置的自由文本结果为 INCONCLUSIVE。
    /// </summary>
    public IReadOnlyList<string> PassingValues { get; init; } = [];
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
    public string? ProductFamilyCode { get; init; }
    public string? ProductCode { get; init; }
    public string? ProcessSpecificationId { get; init; }
    public string? EquipmentId { get; init; }
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
    public required string ProcessSpecificationId { get; init; }
    public string? ProcessSpecification { get; init; }
    public string? ProcessTemplate { get; init; }
    public required string ProcessStep { get; init; }
    public string? ProcessStepName { get; init; }
    public required string PhaseCode { get; init; }
    public bool Required { get; init; } = true;
    public string PhaseSource { get; init; } = "process-specification";
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
    public string? ProductFamilyCode { get; init; }
    public string? ProductCode { get; init; }
    public string? ProcessSpecificationId { get; init; }
    public string? EquipmentId { get; init; }
    public bool Enabled { get; init; } = true;
    public bool UseInComparison { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
