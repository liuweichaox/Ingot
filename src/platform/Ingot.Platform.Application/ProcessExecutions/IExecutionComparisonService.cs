using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>比较生产执行并输出描述性或观察性诊断证据，不宣称因果。</summary>
public interface IExecutionComparisonService
{
    Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
        string executionId,
        CancellationToken ct = default,
        string? siteId = null);

    async Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default,
        string? siteId = null)
    {
        var rows = new Dictionary<string, ExecutionComparisonRow>(StringComparer.Ordinal);
        foreach (var executionId in executionIds.Distinct(StringComparer.Ordinal))
        {
            var row = await GetProcessExecutionAsync(executionId, ct, siteId).ConfigureAwait(false);
            if (row is not null)
                rows[executionId] = row;
        }
        return rows;
    }

    Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
        string executionId,
        int limit,
        CancellationToken ct = default,
        string? siteId = null,
        IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null);

    Task<ExecutionComparisonResult?> CompareSelectedAsync(
        string baselineProcessExecutionId,
        IReadOnlyList<string> executionIds,
        CancellationToken ct = default,
        string? siteId = null,
        IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null);
}
