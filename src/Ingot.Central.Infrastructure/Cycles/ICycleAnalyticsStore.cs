namespace Ingot.Central.Infrastructure.Cycles;

public interface ICycleAnalyticsStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task RebuildCycleAsync(string correlationId, CancellationToken ct = default);
}

