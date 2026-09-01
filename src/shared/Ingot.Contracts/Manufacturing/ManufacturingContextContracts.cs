using System.Text.Json.Serialization;

namespace Ingot.Contracts.Manufacturing;

public sealed record ToolingComponentTypeDefinition
{
    public required string ComponentTypeCode { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ToolingRoleDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public bool Required { get; init; } = true;
    public int SortOrder { get; init; }

    public IReadOnlyList<string> AcceptedComponentTypeCodes { get; init; } = [];
}

public sealed record ToolingTypeDefinition
{
    public required string ToolingTypeCode { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public IReadOnlyList<ToolingRoleDefinition> Roles { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ToolingComponent
{
    public required string ComponentId { get; init; }

    public required string ComponentTypeCode { get; init; }
    public required string SerialNo { get; init; }
    public string? Name { get; init; }
    public string Status { get; init; } = "available";
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ToolingAssembly
{
    public required string ToolingAssemblyId { get; init; }
    public required string ToolingTypeCode { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ToolingAssemblyMember
{
    public required string RoleCode { get; init; }
    public required string ComponentId { get; init; }
}

public sealed record ToolingAssemblyRevision
{
    public Guid AssemblyRevisionId { get; init; }
    public required string ToolingAssemblyId { get; init; }
    // The exact tooling structure version this assembly revision was built against.
    public int ToolingTypeVersion { get; init; }
    public int Revision { get; init; } = 1;
    public IReadOnlyList<ToolingAssemblyMember> Members { get; init; } = [];
    public string? CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

// The route owns the assembly identity; the server owns revision sequencing and audit facts.
public sealed record CreateToolingAssemblyRevisionRequest
{
    public int ToolingTypeVersion { get; init; }
    public IReadOnlyList<ToolingAssemblyMember> Members { get; init; } = [];
}

public sealed record ToolingInstallation
{
    public Guid InstallationId { get; init; }
    public required string EquipmentId { get; init; }
    public Guid AssemblyRevisionId { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public DateTimeOffset? RemovedAt { get; init; }
    public string Source { get; init; } = "manual";
    public string? CommandId { get; init; }
    [JsonPropertyName("userId")]
    public string? Actor { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

// Commands deliberately exclude identifiers, lifecycle end times, and the actor. The server owns those facts.
public sealed record ReplaceToolingInstallationRequest
{
    public required string EquipmentId { get; init; }
    public Guid AssemblyRevisionId { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public string Source { get; init; } = "manual";
    public string? CommandId { get; init; }
}

public sealed record ProductionContext
{
    public Guid ContextId { get; init; }
    public required string EquipmentId { get; init; }
    public required string ProductFamilyCode { get; init; }
    public required string ProductCode { get; init; }
    public required string ProcessSpecificationId { get; init; }
    public required string ProcessSpecificationVersion { get; init; }
    public Guid ToolingInstallationId { get; init; }
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
    public string Source { get; init; } = "manual";

    public string? CommandId { get; init; }
    public string? ExternalOrderRef { get; init; }
    public string? ExternalBatchRef { get; init; }
    public string? MaterialLotRef { get; init; }
    public string? MaterialSpecification { get; init; }

    public string? MaintenanceStatus { get; init; }

    public string? CalibrationStatus { get; init; }
    public string? CalibrationRef { get; init; }
    public DateTimeOffset? CalibrationValidUntil { get; init; }
    [JsonPropertyName("userId")]
    public string? Actor { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

// A production switch always creates the next active context and closes the previous one atomically.
public sealed record ReplaceProductionContextRequest
{
    public required string EquipmentId { get; init; }
    public required string ProductFamilyCode { get; init; }
    public required string ProductCode { get; init; }
    public required string ProcessSpecificationId { get; init; }
    public required string ProcessSpecificationVersion { get; init; }
    public Guid ToolingInstallationId { get; init; }
    public DateTimeOffset ValidFrom { get; init; }
    public string Source { get; init; } = "manual";
    public string? CommandId { get; init; }
    public string? ExternalOrderRef { get; init; }
    public string? ExternalBatchRef { get; init; }
    public string? MaterialLotRef { get; init; }
    public string? MaterialSpecification { get; init; }
    public string? MaintenanceStatus { get; init; }
    public string? CalibrationStatus { get; init; }
    public string? CalibrationRef { get; init; }
    public DateTimeOffset? CalibrationValidUntil { get; init; }
}

public sealed record ResolvedProductionContext
{
    public required ProductionContext Production { get; init; }
    public required ToolingInstallation Installation { get; init; }
    public required ToolingAssemblyRevision AssemblyRevision { get; init; }
    public required ToolingAssembly Assembly { get; init; }
}
