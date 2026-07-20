namespace Ingot.Domain.Events;

/// <summary>
///     Edge 本地事件日志的查询条件。共享过滤字段见 <see cref="EventFilter" />；
///     AfterSeq 是边缘本地单调序号游标（与中心的 AfterIngestId 语义不同）。
/// </summary>
public sealed record EventQuery : EventFilter
{
    public long? AfterSeq { get; init; }
}
