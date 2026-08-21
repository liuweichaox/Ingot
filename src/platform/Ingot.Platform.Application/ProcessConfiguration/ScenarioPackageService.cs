using System.Text.Json;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.Acquisition;
using Ingot.Platform.Application.Inspections;

namespace Ingot.Platform.Application.ProcessConfiguration;

public enum ScenarioPackageOperationStatus
{
    Success,
    Invalid,
    Conflict,
    NotFound
}

public sealed record ScenarioPackageOperationResult
{
    public required ScenarioPackageOperationStatus Status { get; init; }
    public ScenarioPackage? Value { get; init; }
    public ScenarioPackage? Existing { get; init; }
    public string? Error { get; init; }
}

public sealed class ScenarioPackageService(
    IProcessConfigurationStore configurations,
    IIngestionTaskStore ingestionTasks,
    IInspectionMasterDataStore inspectionMasterData)
{
    public Task<IReadOnlyList<ScenarioPackage>> ListAsync(CancellationToken ct = default)
        => configurations.ListScenarioPackagesAsync(ct);

    public Task<ScenarioPackage?> GetAsync(string packageId, int version, CancellationToken ct = default)
        => configurations.GetScenarioPackageAsync(Normalize(packageId), version, ct);

    public async Task<ScenarioPackageOperationResult> UpsertAsync(
        ScenarioPackage? request,
        CancellationToken ct = default)
    {
        if (!ScenarioPackageValidator.TryValidate(request, out var normalized, out var error))
            return Invalid(error);

        var package = normalized!;
        var referenceError = await ValidateReferencesAsync(package, ct).ConfigureAwait(false);
        if (referenceError is not null)
            return Invalid(referenceError);

        var existing = await configurations.GetScenarioPackageAsync(package.PackageId, package.Version, ct)
            .ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && package.Status == ConfigurationStatuses.Retired)
            {
                package = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            }
            else if (SamePayload(existing with { UpdatedAt = default }, package with { UpdatedAt = default }))
            {
                return Success(existing);
            }
            else
            {
                return new ScenarioPackageOperationResult
                {
                    Status = ScenarioPackageOperationStatus.Conflict,
                    Error = "已发布或停用的工艺配置不可修改，请创建新版本。",
                    Existing = existing
                };
            }
        }

        return Success(await configurations.UpsertScenarioPackageAsync(package, ct).ConfigureAwait(false));
    }

    public async Task<ScenarioPackageOperationResult> DeleteAsync(
        string packageId,
        int version,
        CancellationToken ct = default)
    {
        var existing = await configurations.GetScenarioPackageAsync(Normalize(packageId), version, ct)
            .ConfigureAwait(false);
        if (existing is null)
            return new ScenarioPackageOperationResult { Status = ScenarioPackageOperationStatus.NotFound };
        if (existing.Status != ConfigurationStatuses.Draft)
        {
            return new ScenarioPackageOperationResult
            {
                Status = ScenarioPackageOperationStatus.Conflict,
                Error = "只有草稿工艺配置可以删除。",
                Existing = existing
            };
        }

        return await configurations.DeleteScenarioPackageAsync(existing.PackageId, version, ct).ConfigureAwait(false)
            ? Success(existing)
            : new ScenarioPackageOperationResult { Status = ScenarioPackageOperationStatus.NotFound };
    }

    private async Task<string?> ValidateReferencesAsync(ScenarioPackage package, CancellationToken ct)
    {
        var model = await configurations.GetDataModelAsync(package.DataModelId, package.DataModelVersion, ct)
            .ConfigureAwait(false);
        if (model is null)
            return "引用的工艺数据模型版本不存在。";
        var plan = await configurations.GetAnalysisPlanAsync(package.AnalysisPlanId, package.AnalysisPlanVersion, ct)
            .ConfigureAwait(false);
        if (plan is null)
            return "引用的分析方案版本不存在。";
        if (plan.DataModelId != model.ModelId || plan.DataModelVersion != model.Version)
            return "分析方案与工艺配置引用的工艺数据模型版本不一致。";

        foreach (var reference in package.IngestionTasks)
        {
            var task = await ingestionTasks.GetAsync(reference.Id, reference.Version, ct).ConfigureAwait(false);
            if (task is null)
                return $"引用的数据摄取任务不存在：{reference.Id} v{reference.Version}。";
            if (task.DataModelId != model.ModelId || task.DataModelVersion != model.Version)
                return $"数据摄取任务 {reference.Id} v{reference.Version} 使用了不同的工艺数据模型。";
            if (package.Status == ConfigurationStatuses.Published && task.Status != ConfigurationStatuses.Published)
                return $"发布工艺配置前，数据摄取任务 {reference.Id} v{reference.Version} 必须已经发布。";
        }

        if (package.QualityPlan is not null)
        {
            var qualityPlan = await inspectionMasterData.GetInspectionPlanAsync(
                package.QualityPlan.Id, package.QualityPlan.Version, ct).ConfigureAwait(false);
            if (qualityPlan is null)
                return "引用的质量方案版本不存在。";
            if (package.Status == ConfigurationStatuses.Published && qualityPlan.Status != InspectionPlanStatuses.Published)
                return "发布工艺配置前，引用的质量方案必须已经发布。";
        }

        if (package.Status == ConfigurationStatuses.Published &&
            (model.Status != ConfigurationStatuses.Published || plan.Status != ConfigurationStatuses.Published))
        {
            return "发布工艺配置前，引用的工艺数据模型和分析方案必须已经发布。";
        }

        return null;
    }

    private static ScenarioPackageOperationResult Success(ScenarioPackage value) => new()
    { Status = ScenarioPackageOperationStatus.Success, Value = value };

    private static ScenarioPackageOperationResult Invalid(string error) => new()
    { Status = ScenarioPackageOperationStatus.Invalid, Error = error };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}
