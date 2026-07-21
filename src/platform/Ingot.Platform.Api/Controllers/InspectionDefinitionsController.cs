using System.Text.Json;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-definitions")]
public sealed class InspectionDefinitionsController(
    IInspectionMasterDataStore store,
    PlatformUserResolver userResolver) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedRead();
        return denied ?? Ok(new { data = await store.ListInspectionDefinitionsAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{code}/{version:int}")]
    public async Task<IActionResult> Get(string code, int version, CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        var item = await store.GetInspectionDefinitionAsync(code.Trim().ToLowerInvariant(), version, ct)
            .ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionDefinition? request, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        var existing = await store.GetInspectionDefinitionAsync(normalized!.Code, normalized.Version, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var existingPayload = JsonSerializer.Serialize(existing with { UpdatedAt = default }, JsonOptions);
            var requestedPayload = JsonSerializer.Serialize(normalized with { UpdatedAt = default }, JsonOptions);
            return string.Equals(existingPayload, requestedPayload, StringComparison.Ordinal)
                ? Ok(existing)
                : Conflict(new { error = "检测定义版本不可覆盖，请创建新版本。", existing });
        }
        var item = await store.UpsertInspectionDefinitionAsync(normalized!, ct).ConfigureAwait(false);
        return Ok(item);
    }

    [HttpDelete("{code}/{version:int}")]
    public async Task<IActionResult> Delete(string code, int version, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        var normalizedCode = code.Trim().ToLowerInvariant();
        var plans = await store.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        if (plans.Any(plan => plan.Items.Any(item =>
                item.DefinitionCode == normalizedCode && item.DefinitionVersion == version)))
        {
            return Conflict(new { error = "检测定义已被质量方案引用，不能删除。" });
        }
        return await store.DeleteInspectionDefinitionAsync(normalizedCode, version, ct).ConfigureAwait(false)
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
