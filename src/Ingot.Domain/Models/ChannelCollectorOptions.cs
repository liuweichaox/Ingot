namespace Ingot.Domain.Models;

/// <summary>
///     通道采集器配置选项。
/// </summary>
public class ChannelCollectorOptions
{
    /// <summary>
    ///     连接检查失败时的重试延迟（毫秒）。
    /// </summary>
    public int ConnectionCheckRetryDelayMs { get; init; } = 100;

    /// <summary>
    ///     触发条件未满足时的等待延迟（毫秒）。
    /// </summary>
    public int TriggerWaitDelayMs { get; init; } = 100;
}
