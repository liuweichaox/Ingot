// 提供 InspectionRecordsController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Ingot.Contracts.Inspections;
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
        var result = await commands.CreateRecordAsync(request, identity.UserId, ct).ConfigureAwait(false);
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
        return record is null ? ResourceNotFound() : Ok(record);
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
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var query = new InspectionRecordQuery
        {
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
