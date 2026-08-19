using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

public enum InspectionCommandStatus
{
    Success,
    Created,
    Invalid,
    Conflict,
    NotFound
}

public sealed record InspectionCommandResult<T>
{
    public required InspectionCommandStatus Status { get; init; }

    public T? Value { get; init; }

    public object? Existing { get; init; }

    public string? Error { get; init; }

    public static InspectionCommandResult<T> Success(T value) => new()
        { Status = InspectionCommandStatus.Success, Value = value };

    public static InspectionCommandResult<T> Created(T value) => new()
        { Status = InspectionCommandStatus.Created, Value = value };

    public static InspectionCommandResult<T> Invalid(string error) => new()
        { Status = InspectionCommandStatus.Invalid, Error = error };

    public static InspectionCommandResult<T> Conflict(string error, object? existing = null) => new()
        { Status = InspectionCommandStatus.Conflict, Error = error, Existing = existing };

    public static InspectionCommandResult<T> NotFound(string? error = null) => new()
        { Status = InspectionCommandStatus.NotFound, Error = error };
}

public sealed record InspectionAttachmentContent(
    InspectionAttachment Metadata,
    Stream Content);

public sealed partial class InspectionCommands(
    IInspectionMasterDataStore masterData,
    IInspectionRecordStore records,
    IInspectionAttachmentStore attachments,
    IInspectionReviewStore reviews,
    IInspectionWorkflowService workflow)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InspectionCommandResult<InspectionDefinition>> UpsertDefinitionAsync(
        InspectionDefinition? request,
        CancellationToken ct = default)
    {
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return InspectionCommandResult<InspectionDefinition>.Invalid(error);
        var existing = await masterData.GetInspectionDefinitionAsync(
            normalized!.Code, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var existingPayload = JsonSerializer.Serialize(existing with { UpdatedAt = default }, JsonOptions);
            var requestedPayload = JsonSerializer.Serialize(normalized with { UpdatedAt = default }, JsonOptions);
            return string.Equals(existingPayload, requestedPayload, StringComparison.Ordinal)
                ? InspectionCommandResult<InspectionDefinition>.Success(existing)
                : InspectionCommandResult<InspectionDefinition>.Conflict(
                    "检测定义版本不可覆盖，请创建新版本。", existing);
        }
        return InspectionCommandResult<InspectionDefinition>.Success(
            await masterData.UpsertInspectionDefinitionAsync(normalized, ct).ConfigureAwait(false));
    }

    public async Task<InspectionCommandResult<bool>> DeleteDefinitionAsync(
        string code,
        int version,
        CancellationToken ct = default)
    {
        var normalizedCode = code.Trim().ToLowerInvariant();
        var plans = await masterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        if (plans.Any(plan => plan.Items.Any(item =>
                item.DefinitionCode == normalizedCode && item.DefinitionVersion == version)))
        {
            return InspectionCommandResult<bool>.Conflict("检测定义已被质量方案引用，不能删除。");
        }
        return await masterData.DeleteInspectionDefinitionAsync(normalizedCode, version, ct).ConfigureAwait(false)
            ? InspectionCommandResult<bool>.Success(true)
            : InspectionCommandResult<bool>.NotFound();
    }

    public async Task<InspectionCommandResult<InspectionPlan>> UpsertPlanAsync(
        InspectionPlan? request,
        CancellationToken ct = default)
    {
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return InspectionCommandResult<InspectionPlan>.Invalid(error);

        var existing = await masterData.GetInspectionPlanAsync(
            normalized!.PlanId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status is InspectionPlanStatuses.Published or InspectionPlanStatuses.Retired)
        {
            var transitionAllowed = existing.Status == InspectionPlanStatuses.Published &&
                                    normalized.Status == InspectionPlanStatuses.Retired;
            if (transitionAllowed && !normalized.EffectiveTo.HasValue)
                normalized = normalized with { EffectiveTo = DateTimeOffset.UtcNow };
            var existingPayload = JsonSerializer.Serialize(
                existing with
                {
                    UpdatedAt = default,
                    Status = transitionAllowed ? InspectionPlanStatuses.Retired : existing.Status,
                    EffectiveTo = transitionAllowed ? normalized.EffectiveTo : existing.EffectiveTo
                },
                JsonOptions);
            var requestedPayload = JsonSerializer.Serialize(normalized with { UpdatedAt = default }, JsonOptions);
            if (!string.Equals(existingPayload, requestedPayload, StringComparison.Ordinal))
            {
                return InspectionCommandResult<InspectionPlan>.Conflict(
                    "已发布或停用的质量方案不可修改，请创建新版本。", existing);
            }
            if (!transitionAllowed)
                return InspectionCommandResult<InspectionPlan>.Success(existing);
        }

        foreach (var item in normalized.Items)
        {
            if (await masterData.GetInspectionDefinitionAsync(
                    item.DefinitionCode, item.DefinitionVersion, ct).ConfigureAwait(false) is null)
            {
                return InspectionCommandResult<InspectionPlan>.Invalid(
                    $"检测定义不存在：{item.DefinitionCode} v{item.DefinitionVersion}。");
            }
        }

        return InspectionCommandResult<InspectionPlan>.Success(
            await masterData.UpsertInspectionPlanAsync(normalized, ct).ConfigureAwait(false));
    }

    public async Task<InspectionCommandResult<bool>> DeletePlanAsync(
        string planId,
        int version,
        CancellationToken ct = default)
    {
        var normalizedId = planId.Trim().ToLowerInvariant();
        var existing = await masterData.GetInspectionPlanAsync(normalizedId, version, ct).ConfigureAwait(false);
        if (existing is null)
            return InspectionCommandResult<bool>.NotFound();
        if (existing.Status != InspectionPlanStatuses.Draft)
            return InspectionCommandResult<bool>.Conflict("只有草稿质量方案可以删除。");
        return await masterData.DeleteInspectionPlanAsync(normalizedId, version, ct).ConfigureAwait(false)
            ? InspectionCommandResult<bool>.Success(true)
            : InspectionCommandResult<bool>.NotFound();
    }

    public async Task<InspectionCommandResult<InspectionRecord>> CreateRecordAsync(
        CreateInspectionRecordRequest? request,
        string submittedBy,
        CancellationToken ct = default)
    {
        var attributed = request is null ? null : request with { SubmittedBy = submittedBy };
        if (!InspectionRecordValidator.TryValidate(attributed, out var normalized, out var error))
            return InspectionCommandResult<InspectionRecord>.Invalid(error);
        var definition = await masterData.GetInspectionDefinitionAsync(
            normalized!.DefinitionCode, normalized.DefinitionVersion, ct).ConfigureAwait(false);
        if (definition is null)
        {
            return InspectionCommandResult<InspectionRecord>.Invalid(
                $"检测定义不存在：{normalized.DefinitionCode} v{normalized.DefinitionVersion}。");
        }
        if (normalized.SupersedesRecordId.HasValue)
        {
            var original = await records.GetAsync(normalized.SupersedesRecordId.Value, ct).ConfigureAwait(false);
            if (original is null)
                return InspectionCommandResult<InspectionRecord>.Invalid("被更正的检测记录不存在。");
            if (original.ExecutionId != normalized.ExecutionId ||
                original.OutputItemId != normalized.OutputItemId ||
                original.DefinitionCode != normalized.DefinitionCode ||
                original.DefinitionVersion != normalized.DefinitionVersion)
            {
                return InspectionCommandResult<InspectionRecord>.Invalid(
                    "更正记录必须与原记录属于同一工件、运行和检测定义版本。");
            }
            var existingCorrection = await records.GetCorrectionForAsync(original.RecordId, ct).ConfigureAwait(false);
            if (existingCorrection is not null)
            {
                return InspectionCommandResult<InspectionRecord>.Conflict(
                    "该检测记录已经被更正；如需再次更正，请基于当前有效记录创建更正。",
                    existingCorrection);
            }
        }
        if (!TryApplyDefinition(normalized, definition, out normalized, out error))
            return InspectionCommandResult<InspectionRecord>.Invalid(error);
        var task = await workflow.GetTaskAsync(normalized.ExecutionId, ct).ConfigureAwait(false);
        if (task is null)
            return InspectionCommandResult<InspectionRecord>.Invalid("当前分析范围没有匹配的已发布质量方案。");
        var planItem = task.RequiredInspections.FirstOrDefault(item =>
            item.DefinitionCode == normalized.DefinitionCode &&
            item.DefinitionVersion == normalized.DefinitionVersion);
        if (planItem is null)
            return InspectionCommandResult<InspectionRecord>.Invalid("当前质量方案不包含该检测定义版本。");
        if (planItem.RequiresAttachment && normalized.Attachments.Count == 0)
            return InspectionCommandResult<InspectionRecord>.Invalid("当前质量方案要求上传原始附件。");
        foreach (var attachment in normalized.Attachments)
        {
            var stored = await attachments.GetAsync(attachment.AttachmentId, ct).ConfigureAwait(false);
            if (stored is null)
                return InspectionCommandResult<InspectionRecord>.Invalid($"AttachmentId 不存在: {attachment.AttachmentId}");
            if (!string.Equals(stored.Sha256, attachment.Sha256, StringComparison.Ordinal) ||
                !string.Equals(stored.StorageRef, attachment.StorageRef, StringComparison.Ordinal) ||
                stored.SizeBytes != attachment.SizeBytes)
            {
                return InspectionCommandResult<InspectionRecord>.Invalid(
                    $"AttachmentId 元数据与已上传附件不一致: {attachment.AttachmentId}");
            }
        }
        var result = await records.CreateAsync(normalized, submitterVerified: true, ct).ConfigureAwait(false);
        if (result.PayloadConflict)
        {
            return InspectionCommandResult<InspectionRecord>.Conflict(
                "RecordId 已存在，但提交内容不同。检测记录不可原地覆盖。", result.Record);
        }
        return result.Created
            ? InspectionCommandResult<InspectionRecord>.Created(result.Record)
            : InspectionCommandResult<InspectionRecord>.Success(result.Record);
    }

    public async Task<InspectionCommandResult<InspectionReview>> CreateReviewAsync(
        CreateInspectionReviewRequest? request,
        string reviewedBy,
        CancellationToken ct = default)
    {
        if (request is null || request.ReviewId == Guid.Empty || request.ReviewId.Version != 7)
            return InspectionCommandResult<InspectionReview>.Invalid("ReviewId 必须是 UUIDv7。");
        var decision = request.Decision?.Trim().ToUpperInvariant();
        if (!InspectionReviewDecisions.IsValid(decision))
        {
            return InspectionCommandResult<InspectionReview>.Invalid(
                "Decision 必须是 CONFIRMED、REJECTED 或 REINSPECTION_REQUIRED。");
        }
        if (request.Notes?.Length > 2_000)
            return InspectionCommandResult<InspectionReview>.Invalid("Notes 最长为 2000 个字符。");
        var record = await records.GetAsync(request.InspectionRecordId, ct).ConfigureAwait(false);
        if (record is null)
            return InspectionCommandResult<InspectionReview>.NotFound("未找到待复核检测记录。");
        if (string.Equals(record.SubmittedBy, reviewedBy, StringComparison.Ordinal))
            return InspectionCommandResult<InspectionReview>.Invalid("提交者不能复核自己的检测记录。");
        if (record.Attachments.Count == 0)
            return InspectionCommandResult<InspectionReview>.Invalid("视觉复核必须关联原始附件。");
        foreach (var attachment in record.Attachments)
        {
            if (!await attachments.ExistsAsync(attachment.AttachmentId, ct).ConfigureAwait(false))
                return InspectionCommandResult<InspectionReview>.Invalid($"原始附件不可用：{attachment.AttachmentId}");
        }
        var result = await reviews.CreateAsync(
            request with { Decision = decision! }, record.ExecutionId, reviewedBy, ct).ConfigureAwait(false);
        if (result.PayloadConflict)
        {
            return InspectionCommandResult<InspectionReview>.Conflict(
                "ReviewId 已存在但载荷不同，复核记录不可覆盖。", result.Review);
        }
        return result.Created
            ? InspectionCommandResult<InspectionReview>.Created(result.Review)
            : InspectionCommandResult<InspectionReview>.Success(result.Review);
    }

    public async Task<InspectionCommandResult<InspectionScope>> UpsertScopeAsync(
        InspectionScope? request,
        string actor,
        CancellationToken ct = default)
    {
        if (!TryNormalizeScope(request, actor, out var value, out var error))
            return InspectionCommandResult<InspectionScope>.Invalid(error);
        var plan = await masterData.GetInspectionPlanAsync(
            value!.InspectionPlanId, value.InspectionPlanVersion, ct).ConfigureAwait(false);
        if (plan is null || plan.Status != InspectionPlanStatuses.Published)
            return InspectionCommandResult<InspectionScope>.Invalid("质量范围必须绑定已发布的质量方案版本。");
        var existing = await records.GetScopeAsync(value.ScopeId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var page = await records.QueryPageAsync(
                new InspectionRecordQuery { ExecutionId = existing.ScopeId, Limit = 1 }, ct).ConfigureAwait(false);
            if (page.Total > 0)
                return InspectionCommandResult<InspectionScope>.Conflict("该质量范围已经产生检测记录，不能修改。", existing);
            value = value with { CreatedAt = existing.CreatedAt, CreatedBy = existing.CreatedBy };
        }
        return InspectionCommandResult<InspectionScope>.Success(
            await records.UpsertScopeAsync(value, ct).ConfigureAwait(false));
    }

    public async Task<InspectionCommandResult<bool>> DeleteScopeAsync(
        string scopeId,
        CancellationToken ct = default)
    {
        var normalized = scopeId.Trim();
        var existing = await records.GetScopeAsync(normalized, ct).ConfigureAwait(false);
        if (existing is null)
            return InspectionCommandResult<bool>.NotFound();
        var page = await records.QueryPageAsync(
            new InspectionRecordQuery { ExecutionId = normalized, Limit = 1 }, ct).ConfigureAwait(false);
        if (page.Total > 0)
            return InspectionCommandResult<bool>.Conflict("该质量范围已经产生检测记录，不能删除。");
        return await records.DeleteScopeAsync(normalized, ct).ConfigureAwait(false)
            ? InspectionCommandResult<bool>.Success(true)
            : InspectionCommandResult<bool>.NotFound();
    }

    public async Task<InspectionCommandResult<AttachmentUploadResponse>> UploadAttachmentAsync(
        Stream content,
        string fileName,
        string mediaType,
        CancellationToken ct = default)
    {
        try
        {
            return InspectionCommandResult<AttachmentUploadResponse>.Success(
                await attachments.SaveAsync(content, fileName, mediaType, ct).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
        {
            return InspectionCommandResult<AttachmentUploadResponse>.Invalid(exception.Message);
        }
    }

    public async Task<InspectionCommandResult<InspectionAttachmentContent>> OpenAttachmentAsync(
        Guid attachmentId,
        string actor,
        CancellationToken ct = default)
    {
        var attachment = await attachments.GetAsync(attachmentId, ct).ConfigureAwait(false);
        if (attachment is null)
            return InspectionCommandResult<InspectionAttachmentContent>.NotFound();
        var content = await attachments.OpenReadAsync(attachmentId, ct).ConfigureAwait(false);
        if (content is null)
        {
            return InspectionCommandResult<InspectionAttachmentContent>.NotFound(
                "附件元数据存在，但原始文件不可用。");
        }
        await reviews.LogAccessAsync(
            null, attachmentId, "attachment.opened", actor, attachment.Sha256, ct).ConfigureAwait(false);
        return InspectionCommandResult<InspectionAttachmentContent>.Success(
            new InspectionAttachmentContent(attachment, content));
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
            return Fail($"检测特性不属于当前定义版本：{unknown}。", out error);
        var missing = definition.Characteristics.FirstOrDefault(item => item.Required && !submitted.ContainsKey(item.Code));
        if (missing is not null)
            return Fail($"必填检测特性尚未录入：{missing.Name}（{missing.Code}）。", out error);

        var results = new List<InspectionCharacteristicResult>(request.Measurements.Count);
        foreach (var measurement in request.Measurements)
        {
            var characteristic = definitions[measurement.CharacteristicCode];
            if (characteristic.InputType == "numeric")
            {
                if (!measurement.NumericValue.HasValue || measurement.TextValue is not null)
                    return Fail($"检测特性 {characteristic.Name} 必须录入数值。", out error);
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
                return Fail($"检测特性 {characteristic.Name} 必须按{InputTypeLabel(characteristic.InputType)}录入。", out error);
            var textValue = measurement.TextValue.Trim();
            if (characteristic.InputType == "select" &&
                !characteristic.AllowedValues.Contains(textValue, StringComparer.Ordinal))
                return Fail($"检测特性 {characteristic.Name} 的值不在定义选项中。", out error);
            if (characteristic.InputType == "boolean" && textValue is not ("true" or "false"))
                return Fail($"检测特性 {characteristic.Name} 必须选择是或否。", out error);
            results.Add(measurement with
            {
                Outcome = InspectionCharacteristicOutcomeEvaluator.Evaluate(characteristic, textValue),
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

    private static bool TryNormalizeScope(
        InspectionScope? request,
        string actor,
        out InspectionScope? value,
        out string error)
    {
        value = null;
        if (request is null)
            return Fail("质量范围不能为空。", out error);
        var scopeId = request.ScopeId?.Trim() ?? string.Empty;
        var scopeType = request.ScopeType?.Trim().ToLowerInvariant();
        if (!IdPattern().IsMatch(scopeId))
            return Fail("质量范围编号无效。", out error);
        if (scopeType is not ("analysis-window" or "production-run" or "material-lot"))
            return Fail("质量范围类型必须是时间窗口、生产运行段或物料批次。", out error);
        if (request.From == default || request.To == default || request.To <= request.From)
            return Fail("质量范围的结束时间必须晚于开始时间。", out error);
        if (string.IsNullOrWhiteSpace(request.SubjectType) || string.IsNullOrWhiteSpace(request.SubjectId) ||
            string.IsNullOrWhiteSpace(request.OutputItemId) || string.IsNullOrWhiteSpace(request.ProductFamilyCode) ||
            string.IsNullOrWhiteSpace(request.InspectionPlanId) || request.InspectionPlanVersion < 1)
            return Fail("数据对象、质量标识、产品系列和质量方案不能为空。", out error);
        var context = request.Context
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.Trim().ToLowerInvariant(), pair => pair.Value.Trim(), StringComparer.Ordinal);
        context["product_family_code"] = request.ProductFamilyCode.Trim();
        context["quality_scope_type"] = scopeType;
        value = request with
        {
            ScopeId = scopeId,
            ScopeType = scopeType,
            OutputItemId = request.OutputItemId.Trim(),
            SubjectType = request.SubjectType.Trim().ToLowerInvariant(),
            SubjectId = request.SubjectId.Trim(),
            ProductFamilyCode = request.ProductFamilyCode.Trim(),
            InspectionPlanId = request.InspectionPlanId.Trim().ToLowerInvariant(),
            From = request.From.ToUniversalTime(),
            To = request.To.ToUniversalTime(),
            Context = context,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = actor
        };
        error = string.Empty;
        return true;
    }

    private static string InputTypeLabel(string inputType) => inputType switch
    {
        "select" => "选择项",
        "boolean" => "是/否",
        _ => "文本"
    };

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
