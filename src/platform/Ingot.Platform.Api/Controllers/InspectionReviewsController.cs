
// 提供站点隔离的检验复核与审计查询接口。
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-reviews")]
public sealed class InspectionReviewsController(
    InspectionQueries queries,
    InspectionCommands commands,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInspectionReviewRequest? request,
        CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return AuthorizationDenied();
        if (request is null)
            return InvalidRequest("请求不能为空。");
        var target = await queries.GetRecordAsync(request.InspectionRecordId, ct).ConfigureAwait(false);
        if (target is null || !identity.CanAccessSite(target.SiteId))
            return ResourceNotFound("未找到待复核检测记录。");
        var result = await commands.CreateReviewAsync(request, identity.UserId, ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Created => CreatedAtAction(
                nameof(Get), new { reviewId = result.Value!.ReviewId }, result.Value),
            InspectionCommandStatus.Success => Ok(result.Value),
            InspectionCommandStatus.Invalid => InvalidRequest(result.Error),
            InspectionCommandStatus.Conflict => StateConflict(result.Error, ("existing", result.Existing)),
            InspectionCommandStatus.NotFound => ResourceNotFound(result.Error),
            _ => ServerFailure()
        };
    }

    [HttpGet("{reviewId:guid}")]
    public async Task<IActionResult> Get(Guid reviewId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var review = await queries.GetReviewAsync(reviewId, ct).ConfigureAwait(false);
        if (review is null)
            return ResourceNotFound();
        var record = await queries.GetRecordAsync(review.InspectionRecordId, ct).ConfigureAwait(false);
        return record is not null && identity.CanAccessSite(record.SiteId)
            ? Ok(review)
            : ResourceNotFound();
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] Guid? inspectionRecordId,
        [FromQuery] string? executionId,
        [FromQuery] int limit = 200,
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, true, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        if (limit is < 1 or > 500)
            return InvalidRequest("Limit 必须在 1 到 500 之间。");
        var result = await queries.QueryReviewsAsync(inspectionRecordId, executionId, limit, ct).ConfigureAwait(false);
        if (authorizedSiteId is not null)
        {
            var records = await Task.WhenAll(result.Select(review =>
                queries.GetRecordAsync(review.InspectionRecordId, ct))).ConfigureAwait(false);
            var allowedRecordIds = records
                .Where(record => record is not null && string.Equals(
                    record.SiteId, authorizedSiteId, StringComparison.OrdinalIgnoreCase))
                .Select(record => record!.RecordId)
                .ToHashSet();
            result = result.Where(review => allowedRecordIds.Contains(review.InspectionRecordId)).ToArray();
        }
        return Ok(new { data = result, count = result.Count });
    }

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(
        [FromQuery] Guid? inspectionRecordId,
        [FromQuery] Guid? attachmentId,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return AuthorizationDenied();
        if (limit is < 1 or > 500)
            return InvalidRequest("Limit 必须在 1 到 500 之间。");
        if (inspectionRecordId.HasValue)
        {
            var record = await queries.GetRecordAsync(inspectionRecordId.Value, ct).ConfigureAwait(false);
            if (record is null || !identity.CanAccessSite(record.SiteId))
                return ResourceNotFound();
        }
        if (attachmentId.HasValue)
        {
            var attachment = await queries.GetAttachmentAsync(attachmentId.Value, ct).ConfigureAwait(false);
            if (attachment is null || !identity.CanAccessSite(attachment.SiteId))
                return ResourceNotFound();
        }
        if (!inspectionRecordId.HasValue && !attachmentId.HasValue &&
            !identity.HasAnyRole(PlatformRoles.PlatformAdministrator))
        {
            return InvalidRequest("非平台管理员查询审计记录时必须指定检测记录或附件。");
        }
        var result = await queries.QueryAuditAsync(inspectionRecordId, attachmentId, limit, ct).ConfigureAwait(false);
        return Ok(new { data = result, count = result.Count });
    }
}
