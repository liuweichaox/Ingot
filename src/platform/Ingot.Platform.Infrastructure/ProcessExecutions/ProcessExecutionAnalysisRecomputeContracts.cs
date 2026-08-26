// 定义后台分析重算的站点明确执行结果边界。
namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public enum ProcessExecutionAnalysisRecomputeOutcome
{
    Completed,
    Retryable,
    Unsafe
}

/// <summary>在明确站点内重算一次过程执行分析，并报告是否安全完成。</summary>
public interface IProcessExecutionAnalysisRecomputeExecutor
{
    Task<ProcessExecutionAnalysisRecomputeOutcome> RecomputeAnalysisAsync(
        string executionId,
        string siteId,
        CancellationToken ct = default);
}
