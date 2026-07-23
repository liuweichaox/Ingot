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
        if (normalized.SupersedesRecordId.HasValue)
        {
            var original = await store.GetAsync(normalized.SupersedesRecordId.Value, ct).ConfigureAwait(false);
            if (original is null)
                return BadRequest(new { error = "被更正的检测记录不存在。" });
            if (original.OperationRunId != normalized.OperationRunId ||
                original.WorkpieceId != normalized.WorkpieceId ||
                original.DefinitionCode != normalized.DefinitionCode ||
                original.DefinitionVersion != normalized.DefinitionVersion)
            {
                return BadRequest(new { error = "更正记录必须与原记录属于同一工件、运行和检测定义版本。" });
            }
            var existingCorrection = await store.GetCorrectionForAsync(original.RecordId, ct).ConfigureAwait(false);
            if (existingCorrection is not null)
                return Conflict(new { error = "该检测记录已经被更正；如需再次更正，请基于当前有效记录创建更正。", existingCorrection });
        }
        if (!TryApplyDefinition(normalized, definition, out normalized, out error))
            return BadRequest(new { error });
        var task = await workflow.GetTaskAsync(normalized.OperationRunId, ct).ConfigureAwait(false);
        if (task is null)
            return BadRequest(new { error = "当前分析范围没有匹配的已发布质量方案。" });
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

    private static bool TryApplyDefinition(
        CreateInspectionRecordRequest request,
        InspectionDefinition definition,
        out CreateInspectionRecordRequest normalized,
        out string error)
    {
        normalized = request;
        var definitions = definition.Characteristics.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var submitted = request.Measurements.ToDictionary(item => item.CharacteristicCode, StringComparer.Ordinal);
        var unknown = submitted.Keys.FirstOrDefault(code => !definitions.ContainsKey(code));
        if (unknown is not null)
        {
            error = $"检测特性不属于当前定义版本：{unknown}。";
            return false;
        }

        var missing = definition.Characteristics.FirstOrDefault(item => item.Required && !submitted.ContainsKey(item.Code));
        if (missing is not null)
        {
            error = $"必填检测特性尚未录入：{missing.Name}（{missing.Code}）。";
            return false;
        }

        var results = new List<InspectionCharacteristicResult>(request.Measurements.Count);
        foreach (var measurement in request.Measurements)
        {
            var characteristic = definitions[measurement.CharacteristicCode];
            if (characteristic.InputType == "numeric")
            {
                if (!measurement.NumericValue.HasValue || measurement.TextValue is not null)
                {
                    error = $"检测特性 {characteristic.Name} 必须录入数值。";
                    return false;
                }
                var value = measurement.NumericValue.Value;
                var outcome = characteristic.LowerLimit.HasValue && value < characteristic.LowerLimit.Value ||
                              characteristic.UpperLimit.HasValue && value > characteristic.UpperLimit.Value
                    ? "FAIL"
                    : "PASS";
                results.Add(measurement with
                {
                    Outcome = outcome,
                    Unit = characteristic.Unit ?? "1",
                    LowerLimit = characteristic.LowerLimit,
                    UpperLimit = characteristic.UpperLimit
                });
                continue;
            }

            if (measurement.NumericValue.HasValue || string.IsNullOrWhiteSpace(measurement.TextValue))
            {
                error = $"检测特性 {characteristic.Name} 必须按{InputTypeLabel(characteristic.InputType)}录入。";
                return false;
            }
            var textValue = measurement.TextValue.Trim();
            if (characteristic.InputType == "select" && !characteristic.AllowedValues.Contains(textValue, StringComparer.Ordinal))
            {
                error = $"检测特性 {characteristic.Name} 的值不在定义选项中。";
                return false;
            }
            if (characteristic.InputType == "boolean" && textValue is not ("true" or "false"))
            {
                error = $"检测特性 {characteristic.Name} 必须选择是或否。";
                return false;
            }
            results.Add(measurement with
            {
                TextValue = textValue,
                Unit = null,
                LowerLimit = null,
                UpperLimit = null
            });
        }

        var overallOutcome = results.Any(static item => item.Outcome == "FAIL")
            ? "FAIL"
            : results.Any(static item => item.Outcome == "INCONCLUSIVE")
                ? "INCONCLUSIVE"
                : "PASS";
        normalized = request with { Measurements = results, Outcome = overallOutcome };
        error = string.Empty;
        return true;
    }

    private static string InputTypeLabel(string inputType)
        => inputType switch
        {
            "select" => "选择项",
            "boolean" => "是/否",
            _ => "文本"
        };

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
        [FromQuery] int offset = 0,
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
            Limit = limit,
            Offset = offset
        };
        if (!InspectionRecordValidator.TryValidateQuery(query, out var error))
            return BadRequest(new { error });

        var page = await store.QueryPageAsync(query, ct).ConfigureAwait(false);
        return Ok(new { page.Data, count = page.Data.Count, page.Total, page.Offset, page.Limit });
    }
}

