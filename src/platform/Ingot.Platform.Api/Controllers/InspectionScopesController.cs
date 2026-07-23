using System.Text.RegularExpressions;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-scopes")]
public sealed partial class InspectionScopesController(
    IInspectionRecordStore records,
    IInspectionMasterDataStore masterData,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var denied = Denied(readOnly: true);
        return denied ?? Ok(new { data = await records.ListScopesAsync(ct).ConfigureAwait(false) });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionScope? request, CancellationToken ct = default)
    {
        var denied = Denied(readOnly: false);
        if (denied is not null) return denied;
        if (!TryNormalize(request, out var value, out var error))
            return BadRequest(new { error });
        var plan = await masterData.GetInspectionPlanAsync(
            value!.InspectionPlanId,
            value.InspectionPlanVersion,
            ct).ConfigureAwait(false);
        if (plan is null || plan.Status != InspectionPlanStatuses.Published)
            return BadRequest(new { error = "质量范围必须绑定已发布的质量方案版本。" });
        var existing = await records.GetScopeAsync(value.ScopeId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var page = await records.QueryPageAsync(
                new InspectionRecordQuery { OperationRunId = existing.ScopeId, Limit = 1 }, ct).ConfigureAwait(false);
            if (page.Total > 0)
                return Conflict(new { error = "该质量范围已经产生检测记录，不能修改。", existing });
            value = value with { CreatedAt = existing.CreatedAt, CreatedBy = existing.CreatedBy };
        }
        return Ok(await records.UpsertScopeAsync(value, ct).ConfigureAwait(false));
    }

    [HttpDelete("{scopeId}")]
    public async Task<IActionResult> Delete(string scopeId, CancellationToken ct = default)
    {
        var denied = Denied(readOnly: false);
        if (denied is not null) return denied;
        var normalized = scopeId.Trim();
        var existing = await records.GetScopeAsync(normalized, ct).ConfigureAwait(false);
        if (existing is null) return NotFound();
        var page = await records.QueryPageAsync(
            new InspectionRecordQuery { OperationRunId = normalized, Limit = 1 }, ct).ConfigureAwait(false);
        if (page.Total > 0)
            return Conflict(new { error = "该质量范围已经产生检测记录，不能删除。" });
        return await records.DeleteScopeAsync(normalized, ct).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    private IActionResult? Denied(bool readOnly)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null) return Unauthorized(new { error = "需要平台统一认证。" });
        if (readOnly) return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : Forbid();
        return identity.HasAnyRole(
            PlatformRoles.QualityInspector,
            PlatformRoles.QualityReviewer,
            PlatformRoles.ProcessEngineer,
            PlatformRoles.PlatformAdministrator) ? null : Forbid();
    }

    private bool TryNormalize(InspectionScope? request, out InspectionScope? value, out string error)
    {
        value = null;
        if (request is null) return Fail("质量范围不能为空。", out error);
        var scopeId = request.ScopeId?.Trim() ?? string.Empty;
        var scopeType = request.ScopeType?.Trim().ToLowerInvariant();
        if (!IdPattern().IsMatch(scopeId)) return Fail("质量范围编号无效。", out error);
        if (scopeType is not ("analysis-window" or "production-run" or "material-lot"))
            return Fail("质量范围类型必须是时间窗口、生产运行段或物料批次。", out error);
        if (request.From == default || request.To == default || request.To <= request.From)
            return Fail("质量范围的结束时间必须晚于开始时间。", out error);
        if (string.IsNullOrWhiteSpace(request.SubjectType) || string.IsNullOrWhiteSpace(request.SubjectId) ||
            string.IsNullOrWhiteSpace(request.WorkpieceId) || string.IsNullOrWhiteSpace(request.ProductSeries) ||
            string.IsNullOrWhiteSpace(request.InspectionPlanId) || request.InspectionPlanVersion < 1)
            return Fail("数据对象、质量标识、产品系列和质量方案不能为空。", out error);
        var identity = userResolver.ResolveIdentity(User)!;
        var context = request.Context
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim().ToLowerInvariant(), pair => pair.Value.Trim(), StringComparer.Ordinal);
        context["product_series"] = request.ProductSeries.Trim();
        context["quality_scope_type"] = scopeType;
        value = request with
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            WorkpieceId = request.WorkpieceId.Trim(),
            SubjectType = request.SubjectType.Trim().ToLowerInvariant(),
            SubjectId = request.SubjectId.Trim(),
            ProductSeries = request.ProductSeries.Trim(),
            InspectionPlanId = request.InspectionPlanId.Trim().ToLowerInvariant(),
            From = request.From.ToUniversalTime(),
            To = request.To.ToUniversalTime(),
            Context = context,
            CreatedAt = request.CreatedAt == default ? DateTimeOffset.UtcNow : request.CreatedAt.ToUniversalTime(),
            CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? identity.UserId : request.CreatedBy.Trim()
        };
        error = string.Empty;
        return true;
    }

    private static bool Fail(string message, out string error) { error = message; return false; }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
