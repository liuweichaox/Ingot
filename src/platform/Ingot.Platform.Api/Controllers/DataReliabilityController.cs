
// 暴露按授权站点计算的数据可靠性基线。
using Ingot.Contracts.Analytics;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Application.TimeSeries;
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
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, false, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        if (from > to)
            return InvalidRequest("开始时间不能晚于结束时间。");
        if (maximumRuns is < 1 or > 2000)
            return InvalidRequest("MaximumRuns 必须在 1 到 2000 之间。");

        try
        {
            return Ok(await reliability.CalculateAsync(new DataReliabilityBaselineQuery
            {
                SiteId = authorizedSiteId,
                From = from?.ToUniversalTime(),
                To = to?.ToUniversalTime(),
                EdgeId = edgeId,
                EquipmentId = equipmentId,
                MaximumRuns = maximumRuns
            }, ct).ConfigureAwait(false));
        }
        catch (InspectionQueryLimitExceededException exception)
        {
            return UnprocessableRequest(exception.Message);
        }
        catch (PlatformEventQueryLimitExceededException exception)
        {
            return UnprocessableRequest(exception.Message);
        }
        catch (TimeSeriesQueryLimitExceededException exception)
        {
            return UnprocessableRequest(exception.Message);
        }
    }
}
