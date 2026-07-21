using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-plans")]
public sealed class InspectionPlansController(
    IInspectionMasterDataStore store,
    PlatformUserResolver userResolver) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        return Ok(new { data = await store.ListInspectionPlansAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{planId}/{version:int}")]
    public async Task<IActionResult> Get(string planId, int version, CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        var item = await store.GetInspectionPlanAsync(planId.Trim().ToLowerInvariant(), version, ct)
            .ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionPlan? request, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });

        var existing = await store.GetInspectionPlanAsync(normalized!.PlanId, normalized.Version, ct)
            .ConfigureAwait(false);
        if (existing is not null && existing.Status is InspectionPlanStatuses.Published or InspectionPlanStatuses.Retired)
        {
            var transitionAllowed = existing.Status == InspectionPlanStatuses.Published &&
                                    normalized.Status == InspectionPlanStatuses.Retired;
            if (transitionAllowed && !normalized.EffectiveTo.HasValue)
                normalized = normalized with { EffectiveTo = DateTimeOffset.UtcNow };
            var existingPayload = JsonSerializer.Serialize(
                existing with
                {
                    UpdatedAt = default,
                    Status = transitionAllowed ? InspectionPlanStatuses.Retired : existing.Status,
                    EffectiveTo = transitionAllowed ? normalized.EffectiveTo : existing.EffectiveTo
                },
                JsonOptions);
            var requestedPayload = JsonSerializer.Serialize(normalized with { UpdatedAt = default }, JsonOptions);
            if (!string.Equals(existingPayload, requestedPayload, StringComparison.Ordinal))
                return Conflict(new { error = "已发布或停用的质量方案不可修改，请创建新版本。", existing });
            if (!transitionAllowed)
                return Ok(existing);
        }

        foreach (var item in normalized.Items)
        {
            if (await store.GetInspectionDefinitionAsync(item.DefinitionCode, item.DefinitionVersion, ct)
                    .ConfigureAwait(false) is null)
            {
                return BadRequest(new
                {
                    error = $"检测定义不存在：{item.DefinitionCode} v{item.DefinitionVersion}。"
                });
            }
        }

        return Ok(await store.UpsertInspectionPlanAsync(normalized, ct).ConfigureAwait(false));
    }

    [HttpDelete("{planId}/{version:int}")]
    public async Task<IActionResult> Delete(string planId, int version, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        var normalizedId = planId.Trim().ToLowerInvariant();
        var existing = await store.GetInspectionPlanAsync(normalizedId, version, ct).ConfigureAwait(false);
        if (existing is null)
            return NotFound();
        if (existing.Status != InspectionPlanStatuses.Draft)
            return Conflict(new { error = "只有草稿质量方案可以删除。" });
        return await store.DeleteInspectionPlanAsync(normalizedId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }

    private IActionResult? DeniedRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : Forbid();
    }

    private IActionResult? DeniedWrite()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? null
            : Forbid();
    }
}
