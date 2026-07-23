namespace Ingot.Contracts.Manufacturing;

/// <summary>可配置的物理组件分类，例如模芯、模架、刀片或喷嘴。</summary>
public sealed record ToolingComponentTypeDefinition
{
    public required string ComponentTypeCode { get; init; }
    public required string Name { get; init; }
    public string Status { get; init; } = "active";
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>工装类型中的一个可配置装配位置；平台核心不理解具体行业位置名称。</summary>
public sealed record ToolingRoleDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public bool Required { get; init; } = true;
    public int MaxCount { get; init; } = 1;
    public int SortOrder { get; init; }
    /// <summary>允许放入该位置的组件类型；空集合表示不限制。</summary>
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

/// <summary>可单独拆换、复用和分析寿命的物理组件。</summary>
public sealed record ToolingComponent
{
    public required string ComponentId { get; init; }
    /// <summary>组件自身的分类，不代表它在某次装配中的位置或角色。</summary>
    public required string ComponentTypeCode { get; init; }
    public required string SerialNo { get; init; }
    public string? Name { get; init; }
    public string Status { get; init; } = "available";
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>用户可识别且长期稳定的工装/模具身份，不直接保存可变的成员关系。</summary>
public sealed record ToolingAssembly
{
    public required string MoldId { get; init; }
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

/// <summary>某次工装组成的不可变快照；更换任一组件必须创建下一版本。</summary>
public sealed record ToolingAssemblyRevision
{
    public Guid AssemblyRevisionId { get; init; }
    public required string MoldId { get; init; }
    public int Revision { get; init; } = 1;
    public IReadOnlyList<ToolingAssemblyMember> Members { get; init; } = [];
    public string? CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>某个组合版本在设备上的实际有效区间；同一组合再次装入会产生新记录。</summary>
public sealed record ToolingInstallation
{
    public Guid InstallationId { get; init; }
    public required string MachineId { get; init; }
    public Guid AssemblyRevisionId { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public DateTimeOffset? RemovedAt { get; init; }
    public string Source { get; init; } = "manual";
    public string? CommandId { get; init; }
    public string? Actor { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// 设备当前有效的生产信息。它不是 MES 工单；有 MES 时只保存外部引用，无 MES 时由现场在换型时录入一次。
/// </summary>
public sealed record ProductionContext
{
    public Guid ContextId { get; init; }
    public required string MachineId { get; init; }
    public required string ProductSeries { get; init; }
    public required string ProductCode { get; init; }
    public required string RecipeId { get; init; }
    public required string RecipeVersion { get; init; }
    public Guid ToolingInstallationId { get; init; }
    public DateTimeOffset ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
    public string Source { get; init; } = "manual";
    /// <summary>MES/import command id used for idempotent synchronization.</summary>
    public string? CommandId { get; init; }
    public string? ExternalOrderRef { get; init; }
    public string? ExternalBatchRef { get; init; }
    public string? MaterialLotRef { get; init; }
    public string? Actor { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>周期开始时解析出的完整快照，用于写入不可变事件上下文。</summary>
public sealed record ResolvedProductionContext
{
    public required ProductionContext Production { get; init; }
    public required ToolingInstallation Installation { get; init; }
    public required ToolingAssemblyRevision AssemblyRevision { get; init; }
    public required ToolingAssembly Assembly { get; init; }
}
