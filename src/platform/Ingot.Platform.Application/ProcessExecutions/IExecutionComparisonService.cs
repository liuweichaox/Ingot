using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

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
        string? siteId = null);

    Task<ExecutionComparisonResult?> CompareSelectedAsync(
        string baselineProcessExecutionId,
        IReadOnlyList<string> executionIds,
        CancellationToken ct = default,
        string? siteId = null);
}
