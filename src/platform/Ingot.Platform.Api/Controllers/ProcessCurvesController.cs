// 提供 ProcessCurvesController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.TimeSeries;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-executions/{executionId}/curves")]
public sealed class ProcessCurvesController(
    ProcessCurveQueryService curves,
    PlatformUserResolver userResolver) : PlatformApiController
{
    private const int DefaultMaximumPoints = 2_000;
    private const int MaximumSignals = 32;

    [HttpGet]
    public async Task<IActionResult> Query(
        string executionId,
        [FromQuery] string? signalCodes,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int maxPoints = DefaultMaximumPoints,
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
            return InvalidRequest("读取运行曲线必须指定当前身份有权访问的 siteId。");
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return InvalidRequest("运行编号格式不正确。");
        if (from > to)
            return InvalidRequest("曲线开始时间不能晚于结束时间。");
        if (maxPoints is < 100 or > 10_000)
            return InvalidRequest("MaxPoints 必须在 100 到 10000 之间。");

        var requestedSignals = (signalCodes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedSignals.Length == 0)
            return InvalidRequest("至少选择一个过程信号。");
        if (requestedSignals.Length > MaximumSignals || requestedSignals.Any(static code => code.Length > 200))
            return InvalidRequest($"一次最多查询 {MaximumSignals} 个有效信号。");

        return Ok(await curves.QueryAsync(
            authorizedSiteId!,
            executionId.Trim(),
            requestedSignals,
            from,
            to,
            maxPoints,
            ct).ConfigureAwait(false));
    }
}
