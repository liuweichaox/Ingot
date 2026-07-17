using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ingot.Domain.Models;

/// <summary>
///     单个采集通道配置。
/// </summary>
public class AcquisitionChannel
{
    /// <summary>
    ///     通道编码。
    /// </summary>
    public string ChannelCode { get; set; } = string.Empty;

    /// <summary>
    ///     是否启用批量读取。
    /// </summary>
    public bool EnableBatchRead { get; set; } = true;

    /// <summary>
    ///     批量读取起始地址。
    /// </summary>
    public string BatchReadRegister { get; set; } = string.Empty;

    /// <summary>
    ///     批量读取长度。
    /// </summary>
    public ushort BatchReadLength { get; set; }

    /// <summary>
    ///     测量值名称（measurement）。
    /// </summary>
    public string Measurement { get; set; } = string.Empty;

    /// <summary>
    ///     批量聚合大小。
    /// </summary>
    public int BatchSize { get; set; } = 1;

    /// <summary>
    ///     无条件采集间隔，单位毫秒；0 表示尽可能快。
    /// </summary>
    public int AcquisitionInterval { get; set; } = 100;

    /// <summary>
    ///     采集模式。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcquisitionMode AcquisitionMode { get; set; } = AcquisitionMode.Always;

    /// <summary>
    ///     条件采集配置；为空时表示无条件采集。
    /// </summary>
    public ConditionalAcquisition? ConditionalAcquisition { get; set; }

    /// <summary>
    ///     指标定义。
    /// </summary>
    public List<Metric>? Metrics { get; set; }
}
