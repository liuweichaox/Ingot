using System.Text.Json.Serialization;

namespace Ingot.Domain.Models;

/// <summary>
///     条件采集配置，定义开始和结束事件的触发规则。
/// </summary>
public class ConditionalAcquisition
{
    /// <summary>
    ///     用于判断触发条件的寄存器地址，例如 `D6200` 或 `M100`。
    /// </summary>
    public string? Register { get; set; }

    /// <summary>
    ///     触发寄存器的数据类型。
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>
    ///     开始事件触发方式。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcquisitionTrigger? StartTriggerMode { get; set; }

    /// <summary>
    ///     结束事件触发方式。
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcquisitionTrigger? EndTriggerMode { get; set; }
}
