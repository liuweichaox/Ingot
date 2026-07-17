namespace Ingot.Application.Abstractions;

/// <summary>
///     采集周期状态管理器接口。管理条件采集的周期生命周期，为每个周期生成唯一 CycleId。
/// </summary>
public interface IEdgeStateStore : IEdgeContextStore
{
    /// <summary>
    ///     开始一个新的采集周期
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="channelCode">通道编码（ChannelCode）</param>
    /// <param name="measurement">测量值名称（Measurement）</param>
    /// <returns>采集周期对象，包含生成的CycleId</returns>
    AcquisitionCycle StartCycle(string sourceCode, string channelCode, string measurement);

    /// <summary>
    ///     结束一个采集周期
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="channelCode">通道编码（ChannelCode）</param>
    /// <param name="measurement">测量值名称（Measurement）</param>
    /// <returns>采集周期对象，如果不存在则返回null</returns>
    AcquisitionCycle? EndCycle(string sourceCode, string channelCode, string measurement);

    /// <summary>
    ///     获取当前活跃采集周期。
    /// </summary>
    AcquisitionCycle? GetActiveCycle(string sourceCode, string channelCode, string measurement);
}
