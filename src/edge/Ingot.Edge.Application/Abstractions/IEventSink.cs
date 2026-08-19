using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     生产事件唯一写入口。
/// </summary>
public interface IEventSink
{
    /// <summary>
    ///     同步落盘并返回带边缘序号的事件；返回即表示记录已经持久化。
    /// </summary>
    ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default);

    /// <summary>
    ///     在同一事务中批量落盘并返回带边缘序号的事件。
    /// </summary>
    ValueTask<IReadOnlyList<ProductionEvent>> EmitBatchAsync(
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct = default);
}
