using Ingot.Contracts.Analytics;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/data-reliability")]
public sealed class DataReliabilityController(
    IDataReliabilityBaselineService reliability,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet("baseline")]
    public async Task<IActionResult> Baseline(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? edgeId,
        [FromQuery] string? equipmentId,
        [FromQuery] int maximumRuns = 2000,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (from > to)
            return BadRequest(new { error = "开始时间不能晚于结束时间。" });
        if (maximumRuns is < 1 or > 5000)
            return BadRequest(new { error = "MaximumRuns 必须在 1 到 5000 之间。" });

        return Ok(await reliability.CalculateAsync(new DataReliabilityBaselineQuery
        {
            From = from?.ToUniversalTime(),
            To = to?.ToUniversalTime(),
            EdgeId = edgeId,
            EquipmentId = equipmentId,
            MaximumRuns = maximumRuns
        }, ct).ConfigureAwait(false));
    }
}
