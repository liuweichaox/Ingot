// 定义工艺模型、规范、分析方案和发布包的版本化存储端口。
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessConfiguration;

/// <summary>持久化完整的工艺配置注册表，不提供隐式缺省能力。</summary>
public interface IProcessConfigurationStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default);
    Task<ProcessConfigurationMutationResult<ProcessDataModel>> TryUpsertDataModelAsync(
        ProcessDataModel value,
        CancellationToken ct = default);
    Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default);
    Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default);
    Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default);
    Task<ProcessConfigurationDeleteResult> TryDeleteDataModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default);

    Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default);
    Task<ProcessConfigurationMutationResult<ProcessSpecification>> TryUpsertProcessSpecificationAsync(
        ProcessSpecification value,
        CancellationToken ct = default);
    Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default);
    Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default);
    Task<ProcessSpecificationDraftCreationResult> CreateNextProcessSpecificationDraftAsync(
        string processSpecificationId,
        int baseVersion,
        CreateProcessSpecificationDraftRequest request,
        CancellationToken ct = default);
    Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default);
    Task<ProcessConfigurationDeleteResult> TryDeleteProcessSpecificationAsync(
        string processSpecificationId,
        int version,
        CancellationToken ct = default);

    Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default);
    Task<ProcessConfigurationMutationResult<ProcessAnalysisPlan>> TryUpsertAnalysisPlanAsync(
        ProcessAnalysisPlan value,
        CancellationToken ct = default);
    Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default);
    Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default);
    Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default);
    Task<ProcessConfigurationDeleteResult> TryDeleteAnalysisPlanAsync(
        string planId,
        int version,
        CancellationToken ct = default);

    Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default);
    Task<ProcessConfigurationMutationResult<ScenarioPackage>> TryUpsertScenarioPackageAsync(
        ScenarioPackage value,
        CancellationToken ct = default);
    Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default);
    Task<ScenarioPackage?> GetScenarioPackageAsync(string packageId, int version, CancellationToken ct = default);
    Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default);
    Task<ProcessConfigurationDeleteResult> TryDeleteScenarioPackageAsync(
        string packageId,
        int version,
        CancellationToken ct = default);
}
