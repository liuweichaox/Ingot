using System.Text.RegularExpressions;

namespace Ingot.Contracts.Inspections;

public static partial class InspectionRecordValidator
{
    public static bool TryValidate(
        CreateInspectionRecordRequest? request,
        out CreateInspectionRecordRequest? normalized,
        out string error)
    {
        normalized = null;
        if (request is null)
            return Fail("请求不能为空。", out error);
        if (request.RecordId == Guid.Empty || request.RecordId.Version != 7)
            return Fail("RecordId 必须是 UUIDv7。", out error);
        if (!TryNormalizeId(request.WorkpieceId, "WorkpieceId", out var workpieceId, out error) ||
            !TryNormalizeId(request.OperationRunId, "OperationRunId", out var operationRunId, out error) ||
            !TryNormalizeCode(request.DefinitionCode, "DefinitionCode", out var definitionCode, out error) ||
            !TryNormalizeId(request.SubmittedBy, "SubmittedBy", out var submittedBy, out error))
        {
            return false;
        }
        if (request.DefinitionVersion <= 0)
            return Fail("DefinitionVersion 必须大于 0。", out error);
        if (request.MeasuredAt == default || request.RecordedAt == default)
            return Fail("MeasuredAt 和 RecordedAt 不能为空。", out error);
        if (!TryNormalizeOutcome(request.Outcome, out var outcome, out error))
            return false;
        if (request.Measurements is null || request.Measurements.Count > 200)
            return Fail("测量结果列表不能为空，且最多包含 200 项。", out error);
        if (request.Attachments is null || request.Attachments.Count > 50)
            return Fail("附件列表不能为空，且最多包含 50 个文件。", out error);
        if (request.Measurements.Count == 0 && request.Attachments.Count == 0)
            return Fail("检测记录必须至少包含一项结构化结果或附件。", out error);
        if (!TryNormalizeInstrument(request.Instrument, out var instrument, out error))
            return false;

        var measurements = new List<InspectionCharacteristicResult>(request.Measurements.Count);
        foreach (var measurement in request.Measurements)
        {
            if (!TryNormalizeMeasurement(measurement, out var item, out error))
                return false;
            measurements.Add(item!);
        }
        if (measurements.Select(static item => item.CharacteristicCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() != measurements.Count)
        {
            return Fail("同一个检测项目不能重复填写。", out error);
        }

        var attachments = new List<InspectionAttachment>(request.Attachments.Count);
        foreach (var reference in request.Attachments)
        {
            if (!TryNormalizeAttachment(reference, out var item, out error))
                return false;
            attachments.Add(item!);
        }
        if (attachments.Select(static item => item.AttachmentId).Distinct().Count() != attachments.Count)
            return Fail("不能重复添加同一个附件。", out error);

        var notes = NormalizeOptional(request.Notes);
        if (notes?.Length > 2_000)
            return Fail("Notes 最长为 2000 个字符。", out error);
        if (request.SupersedesRecordId == request.RecordId)
            return Fail("检测记录不能更正自身。", out error);
        var correctionReason = NormalizeOptional(request.CorrectionReason);
        if (request.SupersedesRecordId.HasValue && string.IsNullOrWhiteSpace(correctionReason))
            return Fail("更正检测记录时必须填写更正原因。", out error);
        if (correctionReason?.Length > 500)
            return Fail("CorrectionReason 最长为 500 个字符。", out error);

        normalized = request with
        {
            WorkpieceId = workpieceId!,
            OperationRunId = operationRunId!,
            DefinitionCode = definitionCode!,
            MeasuredAt = request.MeasuredAt.ToUniversalTime(),
            RecordedAt = request.RecordedAt.ToUniversalTime(),
            Outcome = outcome!,
            SubmittedBy = submittedBy!,
            Instrument = instrument,
            Measurements = measurements.OrderBy(static item => item.CharacteristicCode, StringComparer.Ordinal).ToArray(),
            Attachments = attachments.OrderBy(static item => item.AttachmentId).ToArray(),
            Notes = notes,
            CorrectionReason = correctionReason
        };
        error = string.Empty;
        return true;
    }

    public static bool TryValidateQuery(InspectionRecordQuery query, out string error)
    {
        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
            return Fail("From 不能晚于 To。", out error);
        if (query.Limit is < 1 or > 500)
            return Fail("Limit 必须在 1 到 500 之间。", out error);
        if (query.Offset < 0)
            return Fail("Offset 不能小于 0。", out error);
        if (query.Outcome is not null && !TryNormalizeOutcome(query.Outcome, out _, out error))
            return false;
        foreach (var (value, name, code) in new[]
                 {
                     (query.WorkpieceId, "WorkpieceId", false),
                     (query.OperationRunId, "OperationRunId", false),
                     (query.DefinitionCode, "DefinitionCode", true)
                 })
        {
            if (value is null)
                continue;
            if (code)
            {
                if (!TryNormalizeCode(value, name, out _, out error))
                    return false;
            }
            else if (!TryNormalizeId(value, name, out _, out error))
            {
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeMeasurement(
        InspectionCharacteristicResult? value,
        out InspectionCharacteristicResult? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("Measurements 不能包含 null。", out error);
        if (!TryNormalizeCode(value.CharacteristicCode, "CharacteristicCode", out var code, out error) ||
            !TryNormalizeOutcome(value.Outcome, out var outcome, out error))
        {
            return false;
        }
        var text = NormalizeOptional(value.TextValue);
        if (!value.NumericValue.HasValue && text is null)
            return Fail($"测量项 {code} 必须包含 NumericValue 或 TextValue。", out error);
        if (text?.Length > 500)
            return Fail($"测量项 {code} 的 TextValue 最长为 500 个字符。", out error);
        if (value.NumericValue.HasValue && string.IsNullOrWhiteSpace(value.Unit))
            return Fail($"数值测量项 {code} 必须提供 Unit。", out error);
        if (value.LowerLimit.HasValue && value.UpperLimit.HasValue &&
            value.LowerLimit > value.UpperLimit)
        {
            return Fail($"测量项 {code} 的 LowerLimit 不能大于 UpperLimit。", out error);
        }
        var unit = NormalizeOptional(value.Unit);
        if (unit?.Length > 32)
            return Fail($"测量项 {code} 的 Unit 最长为 32 个字符。", out error);

        normalized = value with
        {
            CharacteristicCode = code!,
            Outcome = outcome!,
            TextValue = text,
            Unit = unit
        };
        return Succeed(out error);
    }

    private static bool TryNormalizeAttachment(
        InspectionAttachment? value,
        out InspectionAttachment? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("附件列表中包含无效项目。", out error);
        if (value.AttachmentId == Guid.Empty)
            return Fail("附件编号不能为空。", out error);
        var storageRef = NormalizeOptional(value.StorageRef);
        var mediaType = NormalizeOptional(value.MediaType);
        var fileName = NormalizeOptional(value.FileName);
        if (storageRef is null || storageRef.Length > 1_000)
            return Fail("附件保存位置不能为空且最长为 1000 个字符。", out error);
        if (mediaType is null || mediaType.Length > 128 || !MediaTypePattern().IsMatch(mediaType))
            return Fail("附件类型格式不正确。", out error);
        if (fileName is null || fileName.Length > 255)
            return Fail("附件文件名不能为空且最长为 255 个字符。", out error);
        if (value.SizeBytes <= 0)
            return Fail("附件必须包含内容。", out error);
        var hash = value.Sha256?.Trim().ToLowerInvariant();
        if (hash is null || !Sha256Pattern().IsMatch(hash))
            return Fail("附件完整性校验值格式不正确。", out error);

        normalized = value with
        {
            StorageRef = storageRef,
            Sha256 = hash,
            MediaType = mediaType.ToLowerInvariant(),
            FileName = fileName
        };
        return Succeed(out error);
    }

    private static bool TryNormalizeInstrument(
        InspectionInstrumentRef? value,
        out InspectionInstrumentRef? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Succeed(out error);
        if (!TryNormalizeId(value.InstrumentId, "InstrumentId", out var id, out error))
            return false;
        var model = NormalizeOptional(value.Model);
        var calibrationRef = NormalizeOptional(value.CalibrationRef);
        if (model?.Length > 256 || calibrationRef?.Length > 256)
            return Fail("仪器型号和校准引用最长为 256 个字符。", out error);
        normalized = value with
        {
            InstrumentId = id!,
            Model = model,
            CalibrationRef = calibrationRef
        };
        return Succeed(out error);
    }

    private static bool TryNormalizeOutcome(string? value, out string? normalized, out string error)
    {
        normalized = value?.Trim().ToUpperInvariant();
        if (normalized is not ("PASS" or "FAIL" or "INCONCLUSIVE"))
            return Fail("Outcome 只能是 PASS、FAIL 或 INCONCLUSIVE。", out error);
        return Succeed(out error);
    }

    private static bool TryNormalizeId(string? value, string name, out string? normalized, out string error)
    {
        normalized = value?.Trim();
        if (normalized is null || !IdPattern().IsMatch(normalized))
            return Fail($"{name} 只能包含字母、数字、点、下划线、斜杠和连字符，长度为 1 到 128。", out error);
        return Succeed(out error);
    }

    private static bool TryNormalizeCode(string? value, string name, out string? normalized, out string error)
    {
        normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null || !CodePattern().IsMatch(normalized))
            return Fail($"{name} 必须是小写点分标识，长度为 1 到 128。", out error);
        return Succeed(out error);
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Succeed(out string error)
    {
        error = string.Empty;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_./-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]*(?:\\.[a-z0-9][a-z0-9_-]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]*/[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaTypePattern();
}
