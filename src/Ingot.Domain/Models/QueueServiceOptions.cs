namespace Ingot.Domain.Models;

/// <summary>
///     队列服务配置选项。
/// </summary>
public class QueueServiceOptions
{
    /// <summary>
    ///     定时刷新批次间隔（秒）。
    /// </summary>
    public int FlushIntervalSeconds { get; init; } = 5;
}
