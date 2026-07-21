using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-attachments")]
public sealed class InspectionAttachmentsController(
    IInspectionAttachmentStore store,
    IInspectionReviewStore reviews,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityInspector, PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return Forbid();
        if (file is null)
            return BadRequest(new { error = "必须上传名为 file 的 multipart 文件。" });
        await using var stream = file.OpenReadStream();
        try
        {
            var result = await store.SaveAsync(
                stream,
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType)
                    ? "application/octet-stream"
                    : file.ContentType,
                ct).ConfigureAwait(false);
            return Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{attachmentId:guid}")]
    public async Task<IActionResult> Get(Guid attachmentId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        var attachment = await store.GetAsync(attachmentId, ct).ConfigureAwait(false);
        return attachment is null ? NotFound() : Ok(attachment);
    }

    [HttpGet("{attachmentId:guid}/content")]
    public async Task<IActionResult> OpenContent(Guid attachmentId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        var attachment = await store.GetAsync(attachmentId, ct).ConfigureAwait(false);
        if (attachment is null)
            return NotFound();
        var content = await store.OpenReadAsync(attachmentId, ct).ConfigureAwait(false);
        if (content is null)
            return NotFound(new { error = "附件元数据存在，但原始文件不可用。" });
        Response.Headers.ContentDisposition =
            $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}";
        await reviews.LogAccessAsync(
            null,
            attachmentId,
            "attachment.opened",
            identity.UserId,
            attachment.Sha256,
            ct).ConfigureAwait(false);
        return File(content, attachment.MediaType, enableRangeProcessing: true);
    }
}
