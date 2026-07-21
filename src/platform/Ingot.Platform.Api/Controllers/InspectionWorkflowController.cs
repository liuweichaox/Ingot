using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-tasks")]
public sealed class InspectionWorkflowController(
    IInspectionWorkflowService workflow,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "Limit 必须在 1 到 500 之间。" });
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (normalizedStatus is not (null or "all" or "pending" or "in_progress" or "review_pending" or "completed"))
            return BadRequest(new { error = "Status 不在支持范围内。" });
        var tasks = await workflow.QueryTasksAsync(normalizedStatus, limit, ct).ConfigureAwait(false);
        return Ok(new { data = tasks, count = tasks.Count });
    }
}
