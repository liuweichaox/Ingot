namespace Ingot.Domain.Models;

/// <summary>
///     数据采集系统配置选项
/// </summary>
public class AcquisitionOptions
{
    /// <summary>
    ///     通道采集器配置
    /// </summary>
    public ChannelCollectorOptions ChannelCollector { get; set; } = new();

    /// <summary>
    ///     队列服务配置
    /// </summary>
    public QueueServiceOptions QueueService { get; set; } = new();

    /// <summary>
    ///     设备配置服务配置
    /// </summary>
    public DeviceConfigServiceOptions DeviceConfigService { get; set; } = new();
}
