using Ingot.Domain.Events;

namespace Ingot.Platform.Infrastructure.Events;

/// <summary>
///     中心数据存储的查询条件。共享过滤字段见 <see cref="EventFilter" />；
///     AfterIngestId 是中心摄入序号游标（与边缘的 AfterSeq 含义不同）。
/// </summary>
public sealed record PlatformEventQuery : EventFilter
{
    public string? EdgeId { get; init; }
    /// <summary>面向运行目录的受限模糊查找；不替代结构化筛选。</summary>
    public string? SearchText { get; init; }
    public long? AfterIngestId { get; init; }
    public long? BeforeIngestId { get; init; }
    public int Offset { get; init; }
}
