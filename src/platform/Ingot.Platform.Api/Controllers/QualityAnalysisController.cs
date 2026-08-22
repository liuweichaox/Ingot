
// 提供按站点授权和有限扫描的质量分析查询。
using Ingot.Contracts.Analytics;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/quality-analysis")]
public sealed class QualityAnalysisController(
    IQualityAnalysisService analysis,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? productFamilyCode,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? outcome,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, true, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        if (from > to)
            return InvalidRequest("开始时间不能晚于结束时间。");
        if (limit is < 1 or > 1000)
            return InvalidRequest("Limit 必须在 1 到 1000 之间。");
        if (offset < 0)
            return InvalidRequest("Offset 不能小于 0。");
        var normalizedOutcome = Normalize(outcome)?.ToUpperInvariant();
        if (normalizedOutcome is not (null or "PASS" or "FAIL" or "INCONCLUSIVE"))
            return InvalidRequest("Outcome 仅支持 PASS、FAIL 或 INCONCLUSIVE。");

        try
        {
            return Ok(await analysis.QueryAsync(new QualityAnalysisQuery
            {
                SiteId = authorizedSiteId,
                ProductFamilyCode = Normalize(productFamilyCode),
                SubjectType = Normalize(subjectType)?.ToLowerInvariant(),
                SubjectId = Normalize(subjectId),
                Outcome = normalizedOutcome,
                From = from?.ToUniversalTime(),
                To = to?.ToUniversalTime(),
                Limit = limit,
                Offset = offset
            }, ct).ConfigureAwait(false));
        }
        catch (InspectionQueryLimitExceededException exception)
        {
            return UnprocessableRequest(exception.Message);
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
