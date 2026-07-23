using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessConfiguration;

public interface IProcessConfigurationStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default);
    Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default);
    Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default);

    Task<RecipeVersion> UpsertRecipeVersionAsync(RecipeVersion value, CancellationToken ct = default);
    Task<IReadOnlyList<RecipeVersion>> ListRecipeVersionsAsync(CancellationToken ct = default);
    Task<RecipeVersion?> GetRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default);
    Task<bool> DeleteRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default);

    Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default);
    Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default);
    Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default);
}
