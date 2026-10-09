// 为配方建议与机理知识测试提供只读的已发布配方和数据模型。
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Core.Tests.Platform;

internal sealed class RecipeConfigurationTestStore(
    IReadOnlyList<ProcessSpecification> specifications,
    IReadOnlyList<ProcessDataModel> models) : IProcessConfigurationStore
{
    public const string SpecificationId = "optical-press-spec";

    /// <summary>一个已发布配方：保压温度、模压力、保压时间可调，另有温度采集项。</summary>
    public static RecipeConfigurationTestStore OpticalPress()
        => new(
            [
                new ProcessSpecification
                {
                    ProcessSpecificationId = SpecificationId,
                    Version = 1,
                    Name = "光学模压",
                    DataModelId = "optical-press-model",
                    DataModelVersion = 1,
                    Status = ConfigurationStatuses.Published
                }
            ],
            [
                new ProcessDataModel
                {
                    ModelId = "optical-press-model",
                    Version = 1,
                    Name = "光学模压数据模型",
                    Status = ConfigurationStatuses.Published,
                    Acquisition = new AcquisitionModel
                    {
                        DataItems =
                        [
                            new ProcessDataItemDefinition
                                { Code = "temperature", DisplayName = "温度", Unit = "Cel" }
                        ]
                    },
                    ControlParameters =
                    [
                        new ControlParameterDefinition
                        {
                            Code = "holding.temperature", DisplayName = "保压温度", Unit = "Cel",
                            Minimum = 480, Maximum = 550
                        },
                        new ControlParameterDefinition
                        {
                            Code = "holding.time", DisplayName = "保压时间", Unit = "s",
                            Minimum = 5, Maximum = 30
                        },
                        new ControlParameterDefinition
                        {
                            Code = "temperature", DisplayName = "温度设定", Unit = "Cel",
                            Minimum = 480, Maximum = 550
                        },
                        new ControlParameterDefinition
                        {
                            Code = "mold.code", DisplayName = "模具编号", Unit = null,
                            ChangeAllowed = false
                        }
                    ]
                }
            ]);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationMutationResult<ProcessDataModel>> TryUpsertDataModelAsync(
        ProcessDataModel value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default) => Task.FromResult(models);
    public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
        => Task.FromResult(models.SingleOrDefault(value => value.ModelId == modelId && value.Version == version));
    public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationDeleteResult> TryDeleteDataModelAsync(
        string modelId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationMutationResult<ProcessSpecification>> TryUpsertProcessSpecificationAsync(
        ProcessSpecification value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default)
        => Task.FromResult(specifications);
    public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
        => Task.FromResult(specifications.SingleOrDefault(value =>
            value.ProcessSpecificationId == processSpecificationId && value.Version == version));
    public Task<ProcessSpecificationDraftCreationResult> CreateNextProcessSpecificationDraftAsync(
        string processSpecificationId, int baseVersion, CreateProcessSpecificationDraftRequest request,
        CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationDeleteResult> TryDeleteProcessSpecificationAsync(
        string processSpecificationId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationMutationResult<ProcessAnalysisPlan>> TryUpsertAnalysisPlanAsync(
        ProcessAnalysisPlan value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProcessAnalysisPlan>>([]);
    public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
        => Task.FromResult<ProcessAnalysisPlan?>(null);
    public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationDeleteResult> TryDeleteAnalysisPlanAsync(
        string planId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationMutationResult<ScenarioPackage>> TryUpsertScenarioPackageAsync(
        ScenarioPackage value, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ScenarioPackage>>([]);
    public Task<ScenarioPackage?> GetScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
        => Task.FromResult<ScenarioPackage?>(null);
    public Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ProcessConfigurationDeleteResult> TryDeleteScenarioPackageAsync(
        string packageId, int version, CancellationToken ct = default) => throw new NotSupportedException();
}
