using System.Text.Json;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/scenario-packages")]
public sealed class ScenarioPackagesController(
    IProcessConfigurationStore store,
    IAcquisitionProfileStore acquisitionProfiles,
    IInspectionMasterDataStore inspections,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListScenarioPackagesAsync(ct).ConfigureAwait(false) });

    [HttpGet("{packageId}/{version:int}")]
    public async Task<IActionResult> Get(string packageId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var value = await store.GetScenarioPackageAsync(Normalize(packageId), version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ScenarioPackage? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ScenarioPackageValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });

        var package = normalized!;
        var referenceError = await ValidateReferencesAsync(package, ct).ConfigureAwait(false);
        if (referenceError is not null)
            return BadRequest(new { error = referenceError });

        var existing = await store.GetScenarioPackageAsync(package.PackageId, package.Version, ct)
            .ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && package.Status == ConfigurationStatuses.Retired)
                package = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, package with { UpdatedAt = default }))
                return Ok(existing);
            else
                return Conflict(new { error = "已发布或停用的场景包不可修改，请创建新版本。", existing });
        }
        return Ok(await store.UpsertScenarioPackageAsync(package, ct).ConfigureAwait(false));
    }

    [HttpDelete("{packageId}/{version:int}")]
    public async Task<IActionResult> Delete(string packageId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var existing = await store.GetScenarioPackageAsync(Normalize(packageId), version, ct).ConfigureAwait(false);
        if (existing is null)
            return NotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return Conflict(new { error = "只有草稿场景包可以删除。" });
        return await store.DeleteScenarioPackageAsync(existing.PackageId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }

    private async Task<string?> ValidateReferencesAsync(ScenarioPackage package, CancellationToken ct)
    {
        var model = await store.GetDataModelAsync(package.DataModelId, package.DataModelVersion, ct)
            .ConfigureAwait(false);
        if (model is null)
            return "引用的工艺数据模型版本不存在。";
        var plan = await store.GetAnalysisPlanAsync(package.AnalysisPlanId, package.AnalysisPlanVersion, ct)
            .ConfigureAwait(false);
        if (plan is null)
            return "引用的分析方案版本不存在。";
        if (plan.DataModelId != model.ModelId || plan.DataModelVersion != model.Version)
            return "分析方案与场景包引用的工艺数据模型版本不一致。";

        foreach (var reference in package.AcquisitionProfiles)
        {
            var profile = await acquisitionProfiles.GetAsync(reference.Id, reference.Version, ct).ConfigureAwait(false);
            if (profile is null)
                return $"引用的采集配置不存在：{reference.Id} v{reference.Version}。";
            if (profile.DataModelId != model.ModelId || profile.DataModelVersion != model.Version)
                return $"采集配置 {reference.Id} v{reference.Version} 使用了不同的工艺数据模型。";
            if (package.Status == ConfigurationStatuses.Published && profile.Status != ConfigurationStatuses.Published)
                return $"发布场景包前，采集配置 {reference.Id} v{reference.Version} 必须已经发布。";
        }

        if (package.QualityPlan is not null)
        {
            var qualityPlan = await inspections.GetInspectionPlanAsync(
                package.QualityPlan.Id, package.QualityPlan.Version, ct).ConfigureAwait(false);
            if (qualityPlan is null)
                return "引用的质量方案版本不存在。";
            if (package.Status == ConfigurationStatuses.Published && qualityPlan.Status != InspectionPlanStatuses.Published)
                return "发布场景包前，引用的质量方案必须已经发布。";
        }
        if (package.Status == ConfigurationStatuses.Published &&
            (model.Status != ConfigurationStatuses.Published || plan.Status != ConfigurationStatuses.Published))
            return "发布场景包前，引用的工艺数据模型和分析方案必须已经发布。";
        return null;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}
