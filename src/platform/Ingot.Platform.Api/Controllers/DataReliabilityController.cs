// 提供 DataReliabilityController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

using Ingot.Platform.Application.Analytics;
using Ingot.Contracts.Analytics;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/data-reliability")]
public sealed class DataReliabilityController(
    IDataReliabilityBaselineService reliability,
    PlatformUserResolver userResolver) : PlatformApiController
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
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        if (from > to)
            return InvalidRequest("开始时间不能晚于结束时间。");
        if (maximumRuns is < 1 or > 5000)
            return InvalidRequest("MaximumRuns 必须在 1 到 5000 之间。");

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
