// 提供 TimeWindowComparisonsController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/time-window-comparisons")]
public sealed class TimeWindowComparisonsController(
    ITimeWindowComparisonService comparisons,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] TimeWindowComparisonRequest request,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        try
        {
            return Ok(await comparisons.CompareAsync(request, ct).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }
}
