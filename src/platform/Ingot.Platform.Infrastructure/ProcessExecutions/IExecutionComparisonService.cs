using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public interface IExecutionComparisonService
{
    /// <summary>
    ///     读取一个生产过程执行的确定性分析投影。该投影统一包含实际过程执行上下文、
    ///     版本化过程特征、控制参数和质量关联，供比较与优化观察装配共同使用。
    /// </summary>
    Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
        string executionId,
        CancellationToken ct = default);

    async Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default)
    {
        var rows = new Dictionary<string, ExecutionComparisonRow>(StringComparer.Ordinal);
        foreach (var executionId in executionIds.Distinct(StringComparer.Ordinal))
        {
            var row = await GetProcessExecutionAsync(executionId, ct).ConfigureAwait(false);
            if (row is not null)
                rows[executionId] = row;
        }
        return rows;
    }

    Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
        string executionId,
        int limit,
        CancellationToken ct = default);

    Task<ExecutionComparisonResult?> CompareSelectedAsync(
        string baselineProcessExecutionId,
        IReadOnlyList<string> executionIds,
        CancellationToken ct = default);
}
