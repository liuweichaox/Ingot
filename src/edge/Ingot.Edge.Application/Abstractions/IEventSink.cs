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
    ///     批量落盘并返回带边缘序号的事件。默认实现兼容只支持单事件写入的测试/扩展实现。
    /// </summary>
    async ValueTask<IReadOnlyList<ProductionEvent>> EmitBatchAsync(
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        var persisted = new List<ProductionEvent>(events.Count);
        foreach (var evt in events)
            persisted.Add(await EmitAsync(evt, ct).ConfigureAwait(false));
        return persisted;
    }
}
