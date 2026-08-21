using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessConfiguration;

public sealed class ProcessConfigurationApplication(IProcessConfigurationStore configurations)
{
    public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
        => configurations.ListDataModelsAsync(ct);
    public Task<ProcessDataModel?> GetDataModelAsync(string id, int version, CancellationToken ct = default)
        => configurations.GetDataModelAsync(id, version, ct);
    public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
        => configurations.UpsertDataModelAsync(value, ct);
    public Task<bool> DeleteDataModelAsync(string id, int version, CancellationToken ct = default)
        => configurations.DeleteDataModelAsync(id, version, ct);

    public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default)
        => configurations.ListProcessSpecificationsAsync(ct);
    public Task<ProcessSpecification?> GetProcessSpecificationAsync(string id, int version, CancellationToken ct = default)
        => configurations.GetProcessSpecificationAsync(id, version, ct);
    public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default)
        => configurations.UpsertProcessSpecificationAsync(value, ct);
    public Task<bool> DeleteProcessSpecificationAsync(string id, int version, CancellationToken ct = default)
        => configurations.DeleteProcessSpecificationAsync(id, version, ct);

    public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
        => configurations.ListAnalysisPlansAsync(ct);
    public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string id, int version, CancellationToken ct = default)
        => configurations.GetAnalysisPlanAsync(id, version, ct);
    public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default)
        => configurations.UpsertAnalysisPlanAsync(value, ct);
    public Task<bool> DeleteAnalysisPlanAsync(string id, int version, CancellationToken ct = default)
        => configurations.DeleteAnalysisPlanAsync(id, version, ct);

    public Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default)
        => configurations.ListScenarioPackagesAsync(ct);
}
