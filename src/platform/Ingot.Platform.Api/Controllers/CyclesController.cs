using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Cycles;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/cycles")]
public sealed class CyclesController(
    ICycleRecordService cycles,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? productSeries,
        [FromQuery] string? productCode,
        [FromQuery] string? recipeId,
        [FromQuery] string? machineId,
        [FromQuery] string? workpieceId,
        [FromQuery] string? correlationId,
        [FromQuery] string? status,
        [FromQuery] int limit = 200,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (from > to)
            return BadRequest(new { error = "开始时间不能晚于结束时间。" });
        if (status is not (null or "" or "all" or "completed" or "active"))
            return BadRequest(new { error = "Status 仅支持 all、completed 或 active。" });
        if (limit is < 1 or > 1000)
            return BadRequest(new { error = "Limit 必须在 1 到 1000 之间。" });
        if (offset < 0)
            return BadRequest(new { error = "Offset 不能小于 0。" });

        var result = await cycles.QueryAsync(
            from,
            to,
            productSeries,
            productCode,
            recipeId,
            machineId,
            workpieceId,
            correlationId,
            status,
            limit,
            offset,
            ct).ConfigureAwait(false);
        return Ok(result);
    }
}
