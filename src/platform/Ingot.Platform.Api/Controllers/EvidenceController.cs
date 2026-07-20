using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/evidence")]
public sealed class EvidenceController(IInspectionEvidenceStore store) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
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

    [HttpGet("{evidenceId:guid}")]
    public async Task<IActionResult> Get(Guid evidenceId, CancellationToken ct)
    {
        var evidence = await store.GetAsync(evidenceId, ct).ConfigureAwait(false);
        return evidence is null ? NotFound() : Ok(evidence);
    }
}

