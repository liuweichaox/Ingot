namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     指标收集器接口，用于收集系统性能指标
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    ///     记录采集延迟（从数据源读取到写入数据库的时间，毫秒）
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="channelCode">通道编码（可选）</param>
    /// <param name="measurement">测量值名称</param>
    /// <param name="latencyMs">延迟（毫秒）</param>
    void RecordCollectionLatency(string sourceCode, string? channelCode, string measurement, double latencyMs);

    /// <summary>
    ///     记录采集频率（每秒采集的数据点数）
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="channelCode">通道编码（可选）</param>
    /// <param name="measurement">测量值名称</param>
    /// <param name="pointsPerSecond">每秒采集的数据点数</param>
    void RecordCollectionRate(string sourceCode, string? channelCode, string measurement, double pointsPerSecond);

    /// <summary>
    ///     记录队列深度（Channel待读取 + 批量积累的待处理消息总数）
    /// </summary>
    void RecordQueueDepth(int depth);

    /// <summary>
    ///     记录处理延迟（队列处理延迟，毫秒）
    /// </summary>
    void RecordProcessingLatency(double latencyMs);

    /// <summary>
    ///     记录写入延迟（数据库写入延迟，毫秒）
    /// </summary>
    void RecordWriteLatency(string measurement, double latencyMs);

    /// <summary>
    ///     记录批量写入效率（批量大小、写入耗时）
    /// </summary>
    void RecordBatchWriteEfficiency(int batchSize, double latencyMs);

    /// <summary>
    ///     记录错误（按设备/通道统计）
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="channelCode">通道编码（可选）</param>
    /// <param name="measurement">测量值名称（可选）</param>
    void RecordError(string sourceCode, string? channelCode = null, string? measurement = null);

    /// <summary>
    ///     记录数据源连接状态变化
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="isConnected"></param>
    void RecordConnectionStatus(string sourceCode, bool isConnected);

    /// <summary>
    ///     记录连接持续时间（秒）
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="durationSeconds"></param>
    void RecordConnectionDuration(string sourceCode, double durationSeconds);

    /// <summary>记录生产事件已经持久化。</summary>
    void RecordEventEmitted(string eventType, double latencyMs);

    /// <summary>记录事件 outbox 待上行数量。</summary>
    void RecordEventOutboxBacklog(long count);

    /// <summary>记录 outbox 硬上限触发后显式丢弃的事件数量。</summary>
    void RecordEventBacklogDropped(long count);

    /// <summary>记录当前持久化业务上下文项数量。</summary>
    void RecordContextStateEntries(long count);

    /// <summary>记录事件持久化硬故障。</summary>
    void RecordEventPersistenceFailure(string eventType);

    /// <summary>记录事件上行失败。</summary>
    void RecordEventShipFailure(string edgeId);

    /// <summary>记录中心已确认的事件数量与批次延迟。</summary>
    void RecordEventsShipped(string edgeId, int count, double latencyMs);
}
