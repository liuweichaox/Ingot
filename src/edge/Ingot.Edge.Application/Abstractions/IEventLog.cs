using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     不可变生产事件日志。Append 返回即表示记录已经持久化。
/// </summary>
public interface IEventLog
{
    Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default);

    Task<IReadOnlyList<ProductionEvent>> QueryAsync(EventQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(int max, CancellationToken ct = default);

    Task MarkShippedAsync(long upToSeq, CancellationToken ct = default);

    /// <summary>记录一批待上行事件的失败尝试，便于本地审计与诊断。</summary>
    Task IncrementShipAttemptsAsync(long fromSeq, long toSeq, CancellationToken ct = default);

    Task<long> CountPendingAsync(CancellationToken ct = default);

    async Task<EventLogPendingStatistics> GetPendingStatisticsAsync(CancellationToken ct = default)
        => new(await CountPendingAsync(ct).ConfigureAwait(false), null, null, null);
}

public sealed record EventLogPendingStatistics(
    long Count,
    DateTimeOffset? OldestRecordedAt,
    long? CapacityRows,
    long? StorageBytes);
