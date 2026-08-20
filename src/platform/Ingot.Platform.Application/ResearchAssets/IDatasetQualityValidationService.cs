using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

public interface IDatasetQualityValidationService
{
    Task<DatasetQualityValidationReport> RunAsync(
        Stream content,
        string fileName,
        DatasetQualityValidationDatasetManifest manifest,
        string userId,
        CancellationToken ct = default);
}
