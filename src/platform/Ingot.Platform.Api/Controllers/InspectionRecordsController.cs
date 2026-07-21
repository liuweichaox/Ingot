using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-records")]
public sealed class InspectionRecordsController(
    IInspectionRecordStore store,
    IInspectionAttachmentStore attachmentsStore,
    IInspectionMasterDataStore masterData,
    IInspectionWorkflowService workflow,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInspectionRecordRequest? request,
        CancellationToken ct)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityInspector, PlatformRoles.QualityReviewer, PlatformRoles.PlatformAdministrator))
            return Forbid();
        var attributed = request is null ? null : request with { SubmittedBy = identity.UserId };
        if (!InspectionRecordValidator.TryValidate(attributed, out var normalized, out var error))
            return BadRequest(new { error });
        var definition = await masterData.GetInspectionDefinitionAsync(
            normalized!.DefinitionCode,
            normalized.DefinitionVersion,
            ct).ConfigureAwait(false);
        if (definition is null)
            return BadRequest(new { error = $"检测定义不存在：{normalized.DefinitionCode} v{normalized.DefinitionVersion}。" });
        var task = await workflow.GetTaskAsync(normalized.OperationRunId, ct).ConfigureAwait(false);
        if (task is null)
            return BadRequest(new { error = "生产周期没有匹配的已发布质量方案。" });
        var planItem = task.RequiredInspections.FirstOrDefault(item =>
            item.DefinitionCode == normalized.DefinitionCode &&
            item.DefinitionVersion == normalized.DefinitionVersion);
        if (planItem is null)
            return BadRequest(new { error = "当前质量方案不包含该检测定义版本。" });
        if (planItem.RequiresAttachment && normalized.Attachments.Count == 0)
            return BadRequest(new { error = "当前质量方案要求上传原始附件。" });
        foreach (var attachments in normalized!.Attachments)
        {
            var stored = await attachmentsStore.GetAsync(attachments.AttachmentId, ct).ConfigureAwait(false);
            if (stored is null)
                return BadRequest(new { error = $"AttachmentId 不存在: {attachments.AttachmentId}" });
            if (!string.Equals(stored.Sha256, attachments.Sha256, StringComparison.Ordinal) ||
                !string.Equals(stored.StorageRef, attachments.StorageRef, StringComparison.Ordinal) ||
                stored.SizeBytes != attachments.SizeBytes)
            {
                return BadRequest(new { error = $"AttachmentId 元数据与已上传附件不一致: {attachments.AttachmentId}" });
            }
        }
        var result = await store.CreateAsync(
            normalized,
            submitterVerified: true,
            ct).ConfigureAwait(false);
        if (result.PayloadConflict)
        {
            return Conflict(new
            {
                error = "RecordId 已存在，但提交内容不同。检测记录不可原地覆盖。",
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
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
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
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
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

