using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Application.Analytics;

/// <summary>在授权数据范围内生成可复现的质量分析结果。</summary>
public interface IQualityAnalysisService
{
    Task<QualityAnalysisPage> QueryAsync(
        QualityAnalysisQuery query,
        CancellationToken ct = default);
}
