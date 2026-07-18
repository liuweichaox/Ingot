using Ingot.Domain.Events;

namespace Ingot.Central.Infrastructure.Events;

/// <summary>
///     中心事实库的查询条件。共享过滤字段见 <see cref="EventFilter" />；
///     AfterIngestId 是中心摄入序号游标（与边缘的 AfterSeq 语义不同）。
/// </summary>
public sealed record CentralEventQuery : EventFilter
{
    public string? EdgeId { get; init; }
    public long? AfterIngestId { get; init; }
}
