using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public interface ICycleComparisonService
{
    /// <summary>
    ///     读取一个生产周期的确定性分析投影。该投影统一包含实际周期上下文、
    ///     版本化过程特征、配方参数和质量关联，供比较与优化观察装配共同使用。
    /// </summary>
    Task<CycleComparisonRow?> GetCycleAsync(
        string correlationId,
        CancellationToken ct = default);

    async Task<IReadOnlyDictionary<string, CycleComparisonRow>> GetCyclesAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken ct = default)
    {
        var rows = new Dictionary<string, CycleComparisonRow>(StringComparer.Ordinal);
        foreach (var correlationId in correlationIds.Distinct(StringComparer.Ordinal))
        {
            var row = await GetCycleAsync(correlationId, ct).ConfigureAwait(false);
            if (row is not null)
                rows[correlationId] = row;
        }
        return rows;
    }

    Task<CycleComparisonResult?> CompareWithHistoryAsync(
        string correlationId,
        int limit,
        CancellationToken ct = default);

    Task<CycleComparisonResult?> CompareSelectedAsync(
        string baselineCycleId,
        IReadOnlyList<string> cycleIds,
        CancellationToken ct = default);
}
