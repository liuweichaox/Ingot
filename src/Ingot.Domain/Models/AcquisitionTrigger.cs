namespace Ingot.Domain.Models;

/// <summary>
///     条件采集触发方式。
/// </summary>
public enum AcquisitionTrigger
{
    /// <summary>
    ///     当生产序号从 0 变为非 0 时触发开始事件。
    /// </summary>
    RisingEdge,

    /// <summary>
    ///     当生产序号从非 0 变为 0 时触发结束事件。
    /// </summary>
    FallingEdge
}
