using Ingot.Contracts.Analytics;

namespace Ingot.Platform.Infrastructure.Analytics;

public interface IQualityAnalysisService
{
    Task<QualityAnalysisPage> QueryAsync(
        QualityAnalysisQuery query,
        CancellationToken ct = default);
}
