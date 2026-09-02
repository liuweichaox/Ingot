// 定义工艺模型、规范、分析方案和发布包的版本化存储端口。
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessConfiguration;

/// <summary>持久化完整的工艺配置注册表，不提供隐式缺省能力。</summary>
public interface IProcessConfigurationStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default);
    async Task<ProcessConfigurationMutationResult<ProcessDataModel>> TryUpsertDataModelAsync(
        ProcessDataModel value,
        CancellationToken ct = default)
        => ProcessConfigurationMutationResult<ProcessDataModel>.Applied(
            await UpsertDataModelAsync(value, ct).ConfigureAwait(false));
    Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default);
    Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default);
    Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default);
    async Task<ProcessConfigurationDeleteResult> TryDeleteDataModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => await DeleteDataModelAsync(modelId, version, ct).ConfigureAwait(false)
            ? ProcessConfigurationDeleteResult.Applied()
            : ProcessConfigurationDeleteResult.NotFound();

    Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default);
    async Task<ProcessConfigurationMutationResult<ProcessSpecification>> TryUpsertProcessSpecificationAsync(
        ProcessSpecification value,
        CancellationToken ct = default)
        => ProcessConfigurationMutationResult<ProcessSpecification>.Applied(
            await UpsertProcessSpecificationAsync(value, ct).ConfigureAwait(false));
    Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default);
    Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default);
    Task<ProcessSpecificationDraftCreationResult> CreateNextProcessSpecificationDraftAsync(
        string processSpecificationId,
        int baseVersion,
        CreateProcessSpecificationDraftRequest request,
        CancellationToken ct = default)
        => Task.FromException<ProcessSpecificationDraftCreationResult>(
            new NotSupportedException("This process-configuration store does not support atomic draft creation."));
    Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default);
    async Task<ProcessConfigurationDeleteResult> TryDeleteProcessSpecificationAsync(
        string processSpecificationId,
        int version,
        CancellationToken ct = default)
        => await DeleteProcessSpecificationAsync(processSpecificationId, version, ct).ConfigureAwait(false)
            ? ProcessConfigurationDeleteResult.Applied()
            : ProcessConfigurationDeleteResult.NotFound();

    Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default);
    async Task<ProcessConfigurationMutationResult<ProcessAnalysisPlan>> TryUpsertAnalysisPlanAsync(
        ProcessAnalysisPlan value,
        CancellationToken ct = default)
        => ProcessConfigurationMutationResult<ProcessAnalysisPlan>.Applied(
            await UpsertAnalysisPlanAsync(value, ct).ConfigureAwait(false));
    Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default);
    Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default);
    Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default);
    async Task<ProcessConfigurationDeleteResult> TryDeleteAnalysisPlanAsync(
        string planId,
        int version,
        CancellationToken ct = default)
        => await DeleteAnalysisPlanAsync(planId, version, ct).ConfigureAwait(false)
            ? ProcessConfigurationDeleteResult.Applied()
            : ProcessConfigurationDeleteResult.NotFound();

    Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default);
    async Task<ProcessConfigurationMutationResult<ScenarioPackage>> TryUpsertScenarioPackageAsync(
        ScenarioPackage value,
        CancellationToken ct = default)
        => ProcessConfigurationMutationResult<ScenarioPackage>.Applied(
            await UpsertScenarioPackageAsync(value, ct).ConfigureAwait(false));
    Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default);
    Task<ScenarioPackage?> GetScenarioPackageAsync(string packageId, int version, CancellationToken ct = default);
    Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default);
    async Task<ProcessConfigurationDeleteResult> TryDeleteScenarioPackageAsync(
        string packageId,
        int version,
        CancellationToken ct = default)
        => await DeleteScenarioPackageAsync(packageId, version, ct).ConfigureAwait(false)
            ? ProcessConfigurationDeleteResult.Applied()
            : ProcessConfigurationDeleteResult.NotFound();
}

public enum ProcessConfigurationMutationStatus
{
    Applied,
    StateConflict,
    Referenced,
    NotFound
}

public sealed record ProcessConfigurationMutationResult<T>
{
    public required ProcessConfigurationMutationStatus Status { get; init; }

    public T? Value { get; init; }

    public T? Existing { get; init; }

    public bool Succeeded => Status == ProcessConfigurationMutationStatus.Applied;

    public static ProcessConfigurationMutationResult<T> Applied(T value) => new()
    {
        Status = ProcessConfigurationMutationStatus.Applied,
        Value = value
    };

    public static ProcessConfigurationMutationResult<T> StateConflict(T? existing) => new()
    {
        Status = ProcessConfigurationMutationStatus.StateConflict,
        Existing = existing
    };
}

public sealed record ProcessConfigurationDeleteResult
{
    public required ProcessConfigurationMutationStatus Status { get; init; }

    public string? ExistingStatus { get; init; }

    public bool Succeeded => Status == ProcessConfigurationMutationStatus.Applied;

    public static ProcessConfigurationDeleteResult Applied() => new()
    {
        Status = ProcessConfigurationMutationStatus.Applied
    };

    public static ProcessConfigurationDeleteResult NotFound() => new()
    {
        Status = ProcessConfigurationMutationStatus.NotFound
    };

    public static ProcessConfigurationDeleteResult StateConflict(string? existingStatus) => new()
    {
        Status = ProcessConfigurationMutationStatus.StateConflict,
        ExistingStatus = existingStatus
    };

    public static ProcessConfigurationDeleteResult Referenced() => new()
    {
        Status = ProcessConfigurationMutationStatus.Referenced
    };
}
