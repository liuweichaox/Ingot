using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Cycles;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-window-comparisons")]
public sealed class ProcessWindowComparisonsController(
    IProcessWindowComparisonService comparisons,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] ProcessWindowComparisonRequest request,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        try
        {
            return Ok(await comparisons.CompareAsync(request, ct).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
