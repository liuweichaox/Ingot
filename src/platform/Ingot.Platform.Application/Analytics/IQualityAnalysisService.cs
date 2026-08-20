using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Application.Analytics;

public interface IQualityAnalysisService
{
    Task<QualityAnalysisPage> QueryAsync(
        QualityAnalysisQuery query,
        CancellationToken ct = default);
}
