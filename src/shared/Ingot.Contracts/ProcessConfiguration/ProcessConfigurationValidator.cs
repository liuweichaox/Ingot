
using System.Text.RegularExpressions;

namespace Ingot.Contracts.ProcessConfiguration;

public static partial class ProcessConfigurationValidator
{
    private static readonly HashSet<string> SupportedWholeProcessExecutionFeatures = new(
        ["mean", "average", "min", "minimum", "max", "maximum", "range", "std", "stddev",
         "median", "p05", "p95", "integral", "slope"],
        StringComparer.Ordinal);

    public static bool TryValidate(ProcessDataModel? value, out ProcessDataModel? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("工艺数据模型不能为空。", out error);
        if (!TryIdentity(value.ModelId, value.Version, value.Name, value.Status, out var id, out var name, out error))
            return false;
        if (value.Acquisition.DataItems.Count == 0)
            return Fail("工艺数据模型至少需要一个采集数据项。", out error);
        if (!TryNormalizeDataItems(value.Acquisition.DataItems, out var items, out error) ||
            !TryNormalizeParameters(value.ControlParameters, out var parameters, out error))
            return false;
        normalized = value with
        {
            ModelId = id,
            Name = name,
            Description = Clean(value.Description),
            Status = value.Status.Trim().ToLowerInvariant(),
            Acquisition = value.Acquisition with
            {
                DataItems = items
            },
            ControlParameters = parameters,
            UpdatedAt = value.UpdatedAt == default ? DateTimeOffset.UtcNow : value.UpdatedAt
        };
        error = string.Empty;
        return true;
    }

    public static bool TryValidate(ProcessSpecification? value, out ProcessSpecification? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("工艺规范版本不能为空。", out error);
        if (!TryIdentity(value.ProcessSpecificationId, value.Version, value.Name, value.Status, out var id, out var name, out error))
            return false;
        var modelId = NormalizeCode(value.DataModelId);
        if (!ValidCode(modelId) || value.DataModelVersion < 1)
            return Fail("工艺规范版本必须引用有效的工艺数据模型版本。", out error);
        if (value.BasedOnVersion.HasValue && (value.BasedOnVersion < 1 || value.BasedOnVersion == value.Version))
            return Fail("沿用版本必须是不同的正整数版本。", out error);
        var changeReason = Clean(value.ChangeReason);
        if (value.BasedOnVersion.HasValue && changeReason is null)
            return Fail("修订下一版工艺规范时必须说明修订理由。", out error);
        if (changeReason?.Length > 2_000 || Clean(value.MechanismNotes)?.Length > 4_000)
            return Fail("修订理由或机理说明过长。", out error);
        if (!TryNormalizeSelector(value.ContextSelector, out var selector, out error))
            return false;
        if (!TryNormalizeControlParameterValues(value.Values, out var values, out error) ||
            !TryNormalizeEvidenceReferences(value.EvidenceReferences, requireAtLeastOne: false, out var evidenceReferences, out error))
            return false;
        normalized = value with
        {
            ProcessSpecificationId = id,
            Name = name,
            DataModelId = modelId,
            Status = value.Status.Trim().ToLowerInvariant(),
            ContextSelector = selector,
            Values = values,
            ChangeReason = changeReason,
            MechanismNotes = Clean(value.MechanismNotes),
            EvidenceReferences = evidenceReferences,
            UpdatedAt = value.UpdatedAt == default ? DateTimeOffset.UtcNow : value.UpdatedAt
        };
        error = string.Empty;
        return true;
    }

    public static bool TryValidate(
        CreateProcessSpecificationDraftRequest? value,
        out CreateProcessSpecificationDraftRequest? normalized,
        out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("下一版工艺规范请求不能为空。", out error);

        var changeReason = Clean(value.ChangeReason);
        if (changeReason is null)
            return Fail("修订下一版工艺规范时必须说明修订理由。", out error);
        var mechanismNotes = Clean(value.MechanismNotes);
        if (changeReason.Length > 2_000 || mechanismNotes?.Length > 4_000)
            return Fail("修订理由或机理说明过长。", out error);
        if (!TryNormalizeControlParameterValues(value.ParameterOverrides, out var overrides, out error) ||
            !TryNormalizeEvidenceReferences(value.EvidenceReferences, requireAtLeastOne: true, out var evidenceReferences, out error))
            return false;

        normalized = value with
        {
            ChangeReason = changeReason,
            MechanismNotes = mechanismNotes,
            ParameterOverrides = overrides,
            EvidenceReferences = evidenceReferences
        };
        error = string.Empty;
        return true;
    }

    public static bool TryValidate(ProcessAnalysisPlan? value, out ProcessAnalysisPlan? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("分析方案不能为空。", out error);
        if (!TryIdentity(value.PlanId, value.Version, value.Name, value.Status, out var id, out var name, out error))
            return false;
        var modelId = NormalizeCode(value.DataModelId);
        if (!ValidCode(modelId) || value.DataModelVersion < 1)
            return Fail("分析方案必须引用有效的工艺数据模型版本。", out error);
        if (string.IsNullOrWhiteSpace(value.AnalysisScope) || string.IsNullOrWhiteSpace(value.AlignmentMode))
            return Fail("分析范围和对齐方式不能为空。", out error);
        var analysisScope = value.AnalysisScope.Trim().ToLowerInvariant();
        if (analysisScope is not ("production-execution" or "production-run" or "analysis-window"))
            return Fail("分析范围只能是 production-execution、production-run 或 analysis-window。", out error);
        var alignmentMode = value.AlignmentMode.Trim().ToLowerInvariant();
        if (alignmentMode is not ("stage-relative" or "elapsed" or "normalized"))
            return Fail("对齐方式只能是 stage-relative、elapsed 或 normalized。", out error);
        if (value.Signals.Count == 0)
            return Fail("分析方案至少需要一个数据项。", out error);
        if (!TryNormalizeSelector(value.ContextSelector, out var selector, out error))
            return false;
        var normalizedComparisonKeys = value.ComparisonKeys.Select(NormalizeCode).ToArray();
        if (normalizedComparisonKeys.Any(key => !ValidCode(key)))
            return Fail("同类比较键只能包含小写字母、数字、点、下划线和连字符。", out error);
        var comparisonKeys = normalizedComparisonKeys.Distinct(StringComparer.Ordinal).ToArray();
        if (comparisonKeys.Length == 0)
            return Fail("分析方案至少需要一个同类比较键。", out error);
        var knownConfounders = new List<KnownUnmeasuredConfounderDefinition>();
        var confounderCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var confounder in value.KnownUnmeasuredConfounders)
        {
            var code = NormalizeCode(confounder.Code);
            var confounderName = confounder.Name?.Trim() ?? string.Empty;
            if (!ValidCode(code) || !confounderCodes.Add(code) ||
                confounderName.Length is < 1 or > 240)
                return Fail($"潜在未测量混杂因素无效或重复：{confounder.Code}。", out error);
            knownConfounders.Add(confounder with
            {
                Code = code,
                Name = confounderName,
                Description = Clean(confounder.Description)
            });
        }
        var signals = new List<AnalysisSignalSelection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signal in value.Signals)
        {
            var code = NormalizeCode(signal.DataItemCode);
            if (!ValidCode(code) || !seen.Add(code))
                return Fail($"分析数据项编码无效或重复：{signal.DataItemCode}。", out error);
            var features = signal.Features.Select(NormalizeCode).Where(ValidCode).Distinct(StringComparer.Ordinal).ToArray();
            var unsupported = features.FirstOrDefault(feature => !SupportedWholeProcessExecutionFeatures.Contains(feature));
            if (unsupported is not null)
                return Fail($"分析特征不受支持：{unsupported}。", out error);
            signals.Add(signal with { DataItemCode = code, Features = features });
        }
        normalized = value with
        {
            PlanId = id,
            Name = name,
            Description = Clean(value.Description),
            Status = value.Status.Trim().ToLowerInvariant(),
            DataModelId = modelId,
            AnalysisScope = analysisScope,
            AlignmentMode = alignmentMode,
            CohortDimension = Clean(value.CohortDimension)?.ToLowerInvariant(),
            ComparisonKeys = comparisonKeys,
            ContextSelector = selector,
            KnownUnmeasuredConfounders = knownConfounders,
            Signals = signals,
            UpdatedAt = value.UpdatedAt == default ? DateTimeOffset.UtcNow : value.UpdatedAt
        };
        error = string.Empty;
        return true;
    }

    private static bool TryIdentity(
        string rawId,
        int version,
        string rawName,
        string rawStatus,
        out string id,
        out string name,
        out string error)
    {
        id = NormalizeCode(rawId);
        name = rawName?.Trim() ?? string.Empty;
        if (!ValidCode(id))
            return Fail("编码只能包含小写字母、数字、点、下划线和连字符。", out error);
        if (version < 1 || string.IsNullOrWhiteSpace(name))
            return Fail("版本必须大于 0，名称不能为空。", out error);
        if (!ConfigurationStatuses.IsValid(rawStatus?.Trim().ToLowerInvariant()))
            return Fail("配置状态必须是 draft、published 或 retired。", out error);
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeDataItems(
        IReadOnlyList<ProcessDataItemDefinition> source,
        out IReadOnlyList<ProcessDataItemDefinition> result,
        out string error)
    {
        var values = new List<ProcessDataItemDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var code = NormalizeCode(item.Code);
            if (!ValidCode(code) || !seen.Add(code) || string.IsNullOrWhiteSpace(item.DisplayName))
            {
                result = [];
                return Fail($"采集数据项编码无效、重复或缺少来源字段：{item.Code}。", out error);
            }
            values.Add(item with
            {
                Code = code,
                DisplayName = item.DisplayName.Trim(),
                DataType = NormalizeDataType(item.DataType),
                Unit = Clean(item.Unit),
                Category = string.IsNullOrWhiteSpace(item.Category) ? "process" : item.Category.Trim().ToLowerInvariant()
            });
        }
        var stageNumbers = values.Where(static item => item.Category == "stage").ToArray();
        if (stageNumbers.Length > 1)
        {
            result = [];
            return Fail("采集数据项最多只能有一个阶段号。", out error);
        }
        if (stageNumbers is [{ DataType: not "integer" }])
        {
            result = [];
            return Fail("阶段号采集数据项必须使用整数类型。", out error);
        }
        result = values;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeParameters(
        IReadOnlyList<ControlParameterDefinition> source,
        out IReadOnlyList<ControlParameterDefinition> result,
        out string error)
    {
        var values = new List<ControlParameterDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var code = NormalizeCode(item.Code);
            if (!ValidCode(code) || !seen.Add(code) || string.IsNullOrWhiteSpace(item.DisplayName))
            {
                result = [];
                return Fail($"控制参数编码无效、重复或缺少来源字段：{item.Code}。", out error);
            }
            var dataType = NormalizeDataType(item.DataType);
            if (!TryNormalizeParameterBounds(item, dataType, out var minimum, out var maximum, out var step, out error))
            {
                result = [];
                return false;
            }
            values.Add(item with
            {
                Code = code,
                DisplayName = item.DisplayName.Trim(),
                DataType = dataType,
                Unit = Clean(item.Unit),
                Minimum = minimum,
                Maximum = maximum,
                Step = step
            });
        }
        result = values;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeParameterBounds(
        ControlParameterDefinition parameter,
        string dataType,
        out double? minimum,
        out double? maximum,
        out double? step,
        out string error)
    {
        minimum = parameter.Minimum;
        maximum = parameter.Maximum;
        step = parameter.Step;
        if (minimum is { } min && !double.IsFinite(min) ||
            maximum is { } max && !double.IsFinite(max) ||
            step is { } interval && (!double.IsFinite(interval) || interval <= 0))
            return Fail($"控制参数 {parameter.Code} 的边界或步长无效。", out error);
        if (minimum is { } lower && maximum is { } upper && lower > upper)
            return Fail($"控制参数 {parameter.Code} 的最小值不能大于最大值。", out error);
        if (dataType is "boolean" or "string" && (minimum.HasValue || maximum.HasValue || step.HasValue))
            return Fail($"非数值控制参数 {parameter.Code} 不能定义数值边界或步长。", out error);
        if (dataType == "integer" &&
            (minimum is { } integerMinimum && Math.Truncate(integerMinimum) != integerMinimum ||
             maximum is { } integerMaximum && Math.Truncate(integerMaximum) != integerMaximum ||
             step is { } integerStep && Math.Truncate(integerStep) != integerStep))
            return Fail($"整数控制参数 {parameter.Code} 的边界和步长必须是整数。", out error);
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeControlParameterValues(
        IReadOnlyList<ControlParameterValue> source,
        out IReadOnlyList<ControlParameterValue> result,
        out string error)
    {
        var values = new List<ControlParameterValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var code = NormalizeCode(item.Code);
            if (!ValidCode(code) || !seen.Add(code))
            {
                result = [];
                return Fail($"控制参数编码无效或重复：{item.Code}。", out error);
            }
            values.Add(item with { Code = code });
        }
        result = values;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeEvidenceReferences(
        IReadOnlyList<ProcessSpecificationEvidenceReference> source,
        bool requireAtLeastOne,
        out IReadOnlyList<ProcessSpecificationEvidenceReference> result,
        out string error)
    {
        var values = new List<ProcessSpecificationEvidenceReference>();
        var seenReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in source)
        {
            var kind = reference.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
            var referenceId = reference.ReferenceId?.Trim() ?? string.Empty;
            if (kind is not ("process-execution" or "inspection-record" or "analysis-report") ||
                referenceId.Length is < 1 or > 240 || !seenReferences.Add($"{kind}:{referenceId}"))
            {
                result = [];
                return Fail("修订证据引用无效或重复。", out error);
            }
            values.Add(reference with { Kind = kind, ReferenceId = referenceId });
        }
        if (requireAtLeastOne && values.Count == 0)
        {
            result = [];
            return Fail("修订下一版工艺规范时必须引用至少一条证据。", out error);
        }
        result = values;
        error = string.Empty;
        return true;
    }

    private static bool TryNormalizeSelector(
        IReadOnlyDictionary<string, string> source,
        out IReadOnlyDictionary<string, string> result,
        out string error)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in source.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var key = NormalizeCode(pair.Key);
            if (!ValidCode(key) || !values.TryAdd(key, pair.Value.Trim()))
            {
                result = new Dictionary<string, string>();
                return Fail($"上下文键无效或重复：{pair.Key}。", out error);
            }
        }
        result = values;
        error = string.Empty;
        return true;
    }

    private static string NormalizeDataType(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "integer" => "integer",
            "boolean" => "boolean",
            "string" => "string",
            _ => "double"
        };

    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidCode(string value) => CodePattern().IsMatch(value);

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
