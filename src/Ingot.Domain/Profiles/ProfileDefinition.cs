namespace Ingot.Domain.Profiles;

/// <summary>
///     行业 Profile：声明允许的业务对象类型、事件类型及其必需上下文。
/// </summary>
public sealed class ProfileDefinition
{
    public int SchemaVersion { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> ObjectTypes { get; set; } = new();

    public List<ProfileEventType> EventTypes { get; set; } = new();
}

public sealed class ProfileEventType
{
    public string Type { get; set; } = string.Empty;

    public List<string> RequiredContext { get; set; } = new();
}

public sealed class ProfileOptions
{
    public string Directory { get; set; } = "Profiles";
}
