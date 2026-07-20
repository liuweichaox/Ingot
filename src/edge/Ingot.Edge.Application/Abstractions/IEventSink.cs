using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     生产事件唯一写入口。
/// </summary>
public interface IEventSink
{
    /// <summary>
    ///     同步落盘并返回带边缘序号的事件；返回即表示事实已经持久化。
    /// </summary>
    ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default);
}
