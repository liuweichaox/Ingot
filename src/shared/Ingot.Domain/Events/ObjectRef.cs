namespace Ingot.Domain.Events;

/// <summary>
///     生产对象引用。类型由行业 Profile 声明，核心层不硬编码具体设备种类。
/// </summary>
public sealed record ObjectRef
{
    public ObjectRef(string type, string id)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new ArgumentException("对象类型不能为空。", nameof(type));
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("对象标识不能为空。", nameof(id));

        Type = type.Trim();
        Id = id.Trim();
    }

    public string Type { get; init; }

    public string Id { get; init; }
}
