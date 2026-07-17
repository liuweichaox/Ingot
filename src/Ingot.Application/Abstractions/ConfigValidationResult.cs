using System.Collections.Generic;

namespace Ingot.Application.Abstractions;

/// <summary>
///     配置验证结果。
/// </summary>
public class ConfigValidationResult
{
    /// <summary>
    ///     是否有效。
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    ///     错误信息列表。
    /// </summary>
    public List<string> Errors { get; set; } = new();
}
