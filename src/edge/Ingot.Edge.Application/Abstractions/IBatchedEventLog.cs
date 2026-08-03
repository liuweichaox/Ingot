using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     可选的批量事件日志能力。实现应保证整批事件在同一事务中提交。
/// </summary>
public interface IBatchedEventLog
{
    Task<IReadOnlyList<long>> AppendBatchAsync(
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct = default);
}
