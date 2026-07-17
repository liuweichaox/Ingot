namespace Ingot.Domain.Models;

/// <summary>
///     采集周期状态，用于管理条件采集的开始和结束状态
/// </summary>
public class AcquisitionCycle
{
    /// <summary>
    ///     采集周期唯一标识符（GUID）
    /// </summary>
    public required string CycleId { get; init; }

    /// <summary>
    ///     测量值名称（Measurement），时序数据库中的表名/测量值标识
    /// </summary>
    public required string Measurement { get; set; }

    /// <summary>
    ///     数据源编码
    /// </summary>
    public required string SourceCode { get; set; }

    /// <summary>
    ///     通道编码
    /// </summary>
    public required string ChannelCode { get; set; }
}
