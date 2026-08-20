using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>验证研发数据集的覆盖、质量和分析准入条件。</summary>
public interface IDatasetQualityValidationService
{
    Task<DatasetQualityValidationReport> RunAsync(
        Stream content,
        string fileName,
        DatasetQualityValidationDatasetManifest manifest,
        string userId,
        CancellationToken ct = default);
}
