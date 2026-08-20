using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-attachments")]
public sealed class InspectionAttachmentsController(
    InspectionQueries queries,
    InspectionCommands commands,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpPost]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityInspector, PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return AuthorizationDenied();
        if (file is null)
            return InvalidRequest("必须上传名为 file 的 multipart 文件。");
        await using var stream = file.OpenReadStream();
        var result = await commands.UploadAttachmentAsync(
            stream,
            file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Success => Ok(result.Value),
            InspectionCommandStatus.Invalid => InvalidRequest(result.Error),
            _ => ServerFailure()
        };
    }

    [HttpGet("{attachmentId:guid}")]
    public async Task<IActionResult> Get(Guid attachmentId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var attachment = await queries.GetAttachmentAsync(attachmentId, ct).ConfigureAwait(false);
        return attachment is null ? ResourceNotFound() : Ok(attachment);
    }

    [HttpGet("{attachmentId:guid}/content")]
    public async Task<IActionResult> OpenContent(Guid attachmentId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var result = await commands.OpenAttachmentAsync(attachmentId, identity.UserId, ct).ConfigureAwait(false);
        if (result.Status == InspectionCommandStatus.NotFound)
            return result.Error is null ? ResourceNotFound() : ResourceNotFound(result.Error);
        if (result.Status != InspectionCommandStatus.Success || result.Value is null)
            return ServerFailure();
        var attachment = result.Value.Metadata;
        Response.Headers.ContentDisposition =
            $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.FileName)}";
        return File(result.Value.Content, attachment.MediaType, enableRangeProcessing: true);
    }
}
