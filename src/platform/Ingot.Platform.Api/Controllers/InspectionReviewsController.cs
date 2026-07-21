using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-reviews")]
public sealed class InspectionReviewsController(
    IInspectionReviewStore reviews,
    IInspectionRecordStore records,
    IInspectionAttachmentStore attachments,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInspectionReviewRequest? request,
        CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return Forbid();
        if (request is null || request.ReviewId == Guid.Empty || request.ReviewId.Version != 7)
            return BadRequest(new { error = "ReviewId 必须是 UUIDv7。" });
        var decision = request.Decision?.Trim().ToUpperInvariant();
        if (!InspectionReviewDecisions.IsValid(decision))
            return BadRequest(new { error = "Decision 必须是 CONFIRMED、REJECTED 或 REINSPECTION_REQUIRED。" });
        if (request.Notes?.Length > 2_000)
            return BadRequest(new { error = "Notes 最长为 2000 个字符。" });
        var record = await records.GetAsync(request.InspectionRecordId, ct).ConfigureAwait(false);
        if (record is null)
            return NotFound(new { error = "未找到待复核检测记录。" });
        if (record.Attachments.Count == 0)
            return BadRequest(new { error = "视觉复核必须关联原始附件。" });
        foreach (var attachment in record.Attachments)
        {
            if (!await attachments.ExistsAsync(attachment.AttachmentId, ct).ConfigureAwait(false))
                return BadRequest(new { error = $"原始附件不可用：{attachment.AttachmentId}" });
        }
        var result = await reviews.CreateAsync(
            request with { Decision = decision! },
            record.OperationRunId,
            identity.UserId,
            ct).ConfigureAwait(false);
        if (result.PayloadConflict)
            return Conflict(new { error = "ReviewId 已存在但载荷不同，复核记录不可覆盖。", existing = result.Review });
        return result.Created
            ? CreatedAtAction(nameof(Get), new { reviewId = result.Review.ReviewId }, result.Review)
            : Ok(result.Review);
    }

    [HttpGet("{reviewId:guid}")]
    public async Task<IActionResult> Get(Guid reviewId, CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        var review = await reviews.GetAsync(reviewId, ct).ConfigureAwait(false);
        return review is null ? NotFound() : Ok(review);
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] Guid? inspectionRecordId,
        [FromQuery] string? operationRunId,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "Limit 必须在 1 到 500 之间。" });
        var result = await reviews.QueryAsync(inspectionRecordId, operationRunId, limit, ct).ConfigureAwait(false);
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
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return Forbid();
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "Limit 必须在 1 到 500 之间。" });
        var result = await reviews.QueryAuditAsync(inspectionRecordId, attachmentId, limit, ct).ConfigureAwait(false);
        return Ok(new { data = result, count = result.Count });
    }

    private IActionResult? DeniedRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : Forbid();
    }
}
