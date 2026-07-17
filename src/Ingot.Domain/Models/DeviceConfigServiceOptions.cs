namespace Ingot.Domain.Models;

/// <summary>
///     设备配置服务配置选项。
/// </summary>
public class DeviceConfigServiceOptions
{
    /// <summary>
    ///     设备配置目录。相对路径会基于应用程序目录解析。
    /// </summary>
    public string ConfigDirectory { get; set; } = "Configs";

    /// <summary>
    ///     配置变更检测延迟（毫秒）。
    /// </summary>
    public int ConfigChangeDetectionDelayMs { get; set; } = 500;
}
