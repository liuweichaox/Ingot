using Ingot.Central.Api.Inspections;
using Ingot.Central.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-records")]
public sealed class InspectionRecordsController(
    IInspectionRecordStore store,
    InspectionActorTokenValidator tokenValidator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInspectionRecordRequest? request,
        CancellationToken ct)
    {
        if (!InspectionRecordValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        if (!tokenValidator.IsAuthorized(
                normalized!.SubmittedBy,
                Request.Headers.Authorization.FirstOrDefault()))
        {
            return Unauthorized(new { error = "提交者 token 无效。" });
        }

        var result = await store.CreateAsync(
            normalized,
            submitterVerified: tokenValidator.RequiresToken,
            ct).ConfigureAwait(false);
        if (result.PayloadConflict)
        {
            return Conflict(new
            {
                error = "RecordId 已存在，但提交内容不同。检测事实不可原地覆盖。",
                existing = result.Record
            });
        }

        return result.Created
            ? CreatedAtAction(nameof(Get), new { recordId = result.Record.RecordId }, result.Record)
            : Ok(result.Record);
    }

    [HttpGet("{recordId:guid}")]
    public async Task<IActionResult> Get(Guid recordId, CancellationToken ct)
    {
        var record = await store.GetAsync(recordId, ct).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? workpieceId,
        [FromQuery] string? operationRunId,
        [FromQuery] string? definitionCode,
        [FromQuery] string? outcome,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var query = new InspectionRecordQuery
        {
            WorkpieceId = workpieceId?.Trim(),
            OperationRunId = operationRunId?.Trim(),
            DefinitionCode = definitionCode?.Trim().ToLowerInvariant(),
            Outcome = outcome?.Trim().ToUpperInvariant(),
            From = from?.ToUniversalTime(),
            To = to?.ToUniversalTime(),
            Limit = limit
        };
        if (!InspectionRecordValidator.TryValidateQuery(query, out var error))
            return BadRequest(new { error });

        var records = await store.QueryAsync(query, ct).ConfigureAwait(false);
        return Ok(new { data = records, count = records.Count });
    }
}

