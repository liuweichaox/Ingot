// 提供站点隔离且具有累计事件与时序预算的时间窗口比较接口。
using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.TimeSeries;
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
            return InvalidRequest("比较时间窗口必须指定当前身份有权访问的 siteId。");
        try
        {
            return Ok(await comparisons.CompareAsync(request, authorizedSiteId!, ct).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
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
