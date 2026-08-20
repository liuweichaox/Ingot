// 集中校验 ScenarioPackageValidator 的输入、范围和失败条件，调用方不得绕过。

using System.Text.RegularExpressions;

namespace Ingot.Contracts.ProcessConfiguration;

public static partial class ScenarioPackageValidator
{
    public static bool TryValidate(ScenarioPackage? value, out ScenarioPackage? normalized, out string error)
    {
        normalized = null;
        if (value is null)
            return Fail("工艺配置不能为空。", out error);
        var packageId = Code(value.PackageId);
        var dataModelId = Code(value.DataModelId);
        var analysisPlanId = Code(value.AnalysisPlanId);
        var name = value.Name?.Trim() ?? string.Empty;
        var status = value.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!ValidCode(packageId) || value.Version < 1 || string.IsNullOrWhiteSpace(name))
            return Fail("工艺配置编码、版本或名称无效。", out error);
        if (!ConfigurationStatuses.IsValid(status))
            return Fail("工艺配置状态必须是 draft、published 或 retired。", out error);
        if (!ValidCode(dataModelId) || value.DataModelVersion < 1 ||
            !ValidCode(analysisPlanId) || value.AnalysisPlanVersion < 1)
            return Fail("工艺配置必须引用有效的工艺数据模型和分析方案版本。", out error);
        if (!TryReferences(value.IngestionTasks, "数据摄取任务", out var ingestionTasks, out error) ||
            !TryReferences(value.KnowledgeAssets, "知识资产", out var knowledge, out error))
            return false;
        VersionedConfigurationReference? qualityPlan = null;
        if (value.QualityPlan is not null)
        {
            if (!TryReference(value.QualityPlan, out qualityPlan))
                return Fail("质量方案引用无效。", out error);
        }

        var contextFields = new List<ScenarioContextFieldPolicy>();
        var contextCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.ContextFields)
        {
            var code = Code(field.FieldCode);
            var fieldName = field.Name?.Trim() ?? string.Empty;
            var mode = field.Mode?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!ValidCode(code) || !contextCodes.Add(code) || string.IsNullOrWhiteSpace(fieldName) ||
                !ScenarioContextModes.IsValid(mode))
                return Fail($"上下文字段无效或重复：{field.FieldCode}。", out error);
            if (!Rate(field.MinimumCoverage) || !Rate(field.MinimumFactorOverlap))
                return Fail($"上下文字段 {code} 的覆盖率和因素重叠阈值必须在 0 到 1 之间。", out error);
            if (mode != ScenarioContextModes.RecordWhenAvailable && field.MinimumCoverage is null)
                return Fail($"上下文字段 {code} 进入分析或建模时必须声明最低覆盖率。", out error);
            contextFields.Add(field with
            {
                FieldCode = code,
                Name = fieldName,
                Mode = mode
            });
        }

        var constraints = new List<ScenarioConstraintDefinition>();
        var constraintCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constraint in value.Constraints)
        {
            var code = Code(constraint.Code);
            var constraintName = constraint.Name?.Trim() ?? string.Empty;
            var severity = constraint.Severity?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!ValidCode(code) || !constraintCodes.Add(code) || string.IsNullOrWhiteSpace(constraintName) ||
                severity is not ("hard" or "soft"))
                return Fail($"约束无效或重复：{constraint.Code}。", out error);
            if (constraint.Minimum is null && constraint.Maximum is null)
                return Fail($"约束 {code} 至少需要一个边界。", out error);
            if (constraint.Minimum > constraint.Maximum)
                return Fail($"约束 {code} 的下限不能大于上限。", out error);
            constraints.Add(constraint with
            {
                Code = code,
                Name = constraintName,
                Severity = severity,
                Unit = Clean(constraint.Unit)
            });
        }

        var terminology = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in value.Terminology)
        {
            var key = Code(pair.Key);
            var label = pair.Value?.Trim() ?? string.Empty;
            if (!ValidCode(key) || string.IsNullOrWhiteSpace(label) || label.Length > 128 || !terminology.TryAdd(key, label))
                return Fail($"场景术语无效或重复：{pair.Key}。", out error);
        }

        normalized = value with
        {
            PackageId = packageId,
            Name = name,
            Description = Clean(value.Description),
            Status = status,
            DataModelId = dataModelId,
            AnalysisPlanId = analysisPlanId,
            IngestionTasks = ingestionTasks,
            QualityPlan = qualityPlan,
            ContextFields = contextFields,
            Constraints = constraints,
            KnowledgeAssets = knowledge,
            Terminology = terminology,
            UpdatedAt = value.UpdatedAt == default ? DateTimeOffset.UtcNow : value.UpdatedAt.ToUniversalTime()
        };
        error = string.Empty;
        return true;
    }

    private static bool TryReferences(
        IReadOnlyList<VersionedConfigurationReference> values,
        string label,
        out IReadOnlyList<VersionedConfigurationReference> normalized,
        out string error)
    {
        var result = new List<VersionedConfigurationReference>();
        var seen = new HashSet<(string, int)>();
        foreach (var value in values)
        {
            if (!TryReference(value, out var item) || !seen.Add((item!.Id, item.Version)))
            {
                normalized = [];
                return Fail($"{label}引用无效或重复：{value.Id} v{value.Version}。", out error);
            }
            result.Add(item);
        }
        normalized = result;
        error = string.Empty;
        return true;
    }

    private static bool TryReference(VersionedConfigurationReference value, out VersionedConfigurationReference? normalized)
    {
        var id = Code(value.Id);
        normalized = ValidCode(id) && value.Version > 0 ? value with { Id = id } : null;
        return normalized is not null;
    }

    private static bool Rate(double? value) => value is null or (>= 0 and <= 1);
    private static string Code(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidCode(string value) => CodePattern().IsMatch(value);
    private static bool Fail(string message, out string error) { error = message; return false; }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
