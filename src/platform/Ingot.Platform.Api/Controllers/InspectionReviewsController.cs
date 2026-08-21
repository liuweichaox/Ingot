
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
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        var review = await queries.GetReviewAsync(reviewId, ct).ConfigureAwait(false);
        return review is null ? ResourceNotFound() : Ok(review);
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] Guid? inspectionRecordId,
        [FromQuery] string? executionId,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        if (limit is < 1 or > 500)
            return InvalidRequest("Limit 必须在 1 到 500 之间。");
        var result = await queries.QueryReviewsAsync(inspectionRecordId, executionId, limit, ct).ConfigureAwait(false);
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
        var result = await queries.QueryAuditAsync(inspectionRecordId, attachmentId, limit, ct).ConfigureAwait(false);
        return Ok(new { data = result, count = result.Count });
    }

    private IActionResult? DeniedRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : AuthorizationDenied();
    }
}
