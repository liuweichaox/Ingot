using System.Text.Json;
using Ingot.Agent;
using Ingot.Central.Api.Agents;
using Ingot.Connector.Builder;
using Ingot.Contracts.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/connector-workspaces")]
public sealed class ConnectorWorkspacesController(
    IConnectorWorkspaceService workspaces,
    IAgentArtifactStore artifacts,
    AgentTokenValidator tokenValidator) : ControllerBase
{
    [HttpGet("{workspaceId}")]
    public async Task<IActionResult> Get(string workspaceId, CancellationToken ct)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        try
        {
            var workspace = await workspaces.GetAsync(actorId!, workspaceId, ct).ConfigureAwait(false);
            return workspace is null ? NotFound() : Ok(workspace);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{workspaceId}/files")]
    public async Task<IActionResult> ListFiles(string workspaceId, CancellationToken ct)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        try
        {
            var files = await workspaces.ListFilesAsync(actorId!, workspaceId, ct).ConfigureAwait(false);
            return Ok(new { workspaceId, files });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{workspaceId}/file")]
    public async Task<IActionResult> ReadFile(string workspaceId, [FromQuery] string path, CancellationToken ct)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        try
        {
            var content = await workspaces.ReadFileAsync(actorId!, workspaceId, path, ct).ConfigureAwait(false);
            return Ok(new { workspaceId, path, content });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{workspaceId}:approve-package")]
    public async Task<IActionResult> ApprovePackage(string workspaceId, CancellationToken ct)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        if (!tokenValidator.CanApprovePackaging(actorId!))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "当前 Actor 没有打包批准权限。" });
        try
        {
            return Ok(await workspaces.ApprovePackagingAsync(actorId!, workspaceId, ct).ConfigureAwait(false));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpPost("{workspaceId}:package")]
    public async Task<IActionResult> Package(string workspaceId, CancellationToken ct)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        try
        {
            var result = await workspaces.PackageAsync(actorId!, workspaceId, ct).ConfigureAwait(false);
            var artifact = await artifacts.SaveAsync(
                actorId!,
                result.Workspace.RunId,
                AgentArtifactKinds.ConnectorPackage,
                result.Package.PackageName,
                "zip-reference",
                JsonSerializer.Serialize(result.Package),
                JsonSerializer.SerializeToElement(new
                {
                    result.Package.WorkspaceId,
                    result.Package.Sha256,
                    result.Package.SizeBytes
                }),
                ct).ConfigureAwait(false);
            return Ok(new { workspace = result.Workspace, package = result.Package, artifact });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    [HttpGet("{workspaceId}/package")]
    public async Task<IActionResult> DownloadPackage(string workspaceId, CancellationToken ct)
    {
        if (!TryAuthorize(out var actorId, out var error))
            return error!;
        try
        {
            var package = await workspaces.OpenPackageAsync(actorId!, workspaceId, ct).ConfigureAwait(false);
            Response.Headers.ETag = $"\"{package.Sha256}\"";
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            return File(package.Content, "application/zip", package.FileName, enableRangeProcessing: true);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidDataException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    private bool TryAuthorize(out string? actorId, out IActionResult? error)
    {
        if (!AgentTokenValidator.IsDesktopClient(Request.Headers["X-Ingot-Client"].FirstOrDefault()))
        {
            actorId = null;
            error = StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = $"此接口仅供 Ingot Agent Desktop 使用，必须提供 X-Ingot-Client: {AgentTokenValidator.DesktopClientId}。"
            });
            return false;
        }
        actorId = Request.Headers["X-Ingot-Actor"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            error = BadRequest(new { error = "必须提供 X-Ingot-Actor。" });
            return false;
        }
        if (!tokenValidator.IsAuthorized(actorId, Request.Headers.Authorization.FirstOrDefault()))
        {
            error = Unauthorized(new { error = "连接器工作区访问凭据无效。" });
            return false;
        }
        actorId = tokenValidator.CanonicalizeActorId(actorId);
        error = null;
        return true;
    }
}
