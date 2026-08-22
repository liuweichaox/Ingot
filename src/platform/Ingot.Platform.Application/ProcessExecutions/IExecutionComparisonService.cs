// 定义运行详情与同条件比较所需的应用查询边界。
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>读取站点授权范围内可用于工程比较的过程执行。</summary>
public interface IExecutionComparisonService
{
    Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
        string executionId,
        CancellationToken ct = default,
        string? siteId = null);

    Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default,
        string? siteId = null);

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
