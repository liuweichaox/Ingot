using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

public interface IEventLog
{
    Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default);

    Task<IReadOnlyList<long>> AppendBatchAsync(
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProductionEvent>> QueryAsync(EventQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(int max, CancellationToken ct = default);

    Task MarkShippedAsync(long upToSeq, CancellationToken ct = default);

    Task IncrementShipAttemptsAsync(long fromSeq, long toSeq, CancellationToken ct = default);

    Task QuarantineAsync(long seq, string reason, CancellationToken ct = default);

    Task<long> CountPendingAsync(CancellationToken ct = default);

    Task<EventLogPendingStatistics> GetPendingStatisticsAsync(CancellationToken ct = default);
}

public sealed record EventLogPendingStatistics(
    long Count,
    DateTimeOffset? OldestRecordedAt,
    long? CapacityRows,
    long? StorageBytes);
