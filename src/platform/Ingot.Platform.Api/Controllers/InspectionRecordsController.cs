
// 提供站点隔离的检验记录写入和分页查询接口。
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-records")]
public sealed class InspectionRecordsController(
    InspectionQueries queries,
    InspectionCommands commands,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInspectionRecordRequest? request,
        CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityInspector, PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, request?.SiteId, false, out var siteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", request?.SiteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("创建检测记录必须指定当前身份有权访问的 siteId。");
        var result = await commands.CreateRecordAsync(request, identity.UserId, ct, siteId).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Created => CreatedAtAction(
                nameof(Get), new { recordId = result.Value!.RecordId }, result.Value),
            InspectionCommandStatus.Success => Ok(result.Value),
            InspectionCommandStatus.Invalid => InvalidRequest(result.Error),
            InspectionCommandStatus.Conflict => StateConflict(result.Error, ("existing", result.Existing)),
            InspectionCommandStatus.NotFound => ResourceNotFound(result.Error),
            _ => ServerFailure()
        };
    }

    [HttpGet("{recordId:guid}")]
    public async Task<IActionResult> Get(Guid recordId, CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var record = await queries.GetRecordAsync(recordId, ct).ConfigureAwait(false);
        if (record is null)
            return ResourceNotFound();
        return identity.CanAccessSite(record.SiteId)
            ? Ok(record)
            : ResourceNotFound();
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? outputItemId,
        [FromQuery] string? executionId,
        [FromQuery] string? definitionCode,
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
        var query = new InspectionRecordQuery
        {
            SiteId = authorizedSiteId,
            OutputItemId = outputItemId?.Trim(),
            ExecutionId = executionId?.Trim(),
            DefinitionCode = definitionCode?.Trim().ToLowerInvariant(),
            Outcome = outcome?.Trim().ToUpperInvariant(),
            From = from?.ToUniversalTime(),
            To = to?.ToUniversalTime(),
            Limit = limit,
            Offset = offset
        };
        var result = await queries.QueryRecordsAsync(query, ct).ConfigureAwait(false);
        if (result.Status == InspectionCommandStatus.Invalid)
            return InvalidRequest(result.Error);
        var page = result.Value!;
        return Ok(new { page.Data, count = page.Data.Count, page.Total, page.Offset, page.Limit });
    }
}
