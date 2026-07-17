namespace Ingot.Application.Abstractions;

/// <summary>
///     配置变更类型。
/// </summary>
public enum ConfigChangeType
{
    /// <summary>
    ///     新增配置。
    /// </summary>
    Added,

    /// <summary>
    ///     更新配置。
    /// </summary>
    Updated,

    /// <summary>
    ///     删除配置。
    /// </summary>
    Removed
}
