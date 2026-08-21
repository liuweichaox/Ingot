
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed partial class ProcessResearchWorkflow
{
    private static ResearchOptimizationFeatureSet NormalizeOptimizationFeatures(
        ResearchOptimizationFeatureSet? value,
        IEnumerable<string> controlVariableCodes)
    {
        value ??= new ResearchOptimizationFeatureSet();
        if (value.Version < 1)
            throw new ProcessResearchRuleException("优化特征集版本必须大于 0。");
        if (value.DerivedFeatures.Count > 100)
            throw new ProcessResearchRuleException("单个优化特征集最多包含 100 个派生特征。");

        var availableInputs = controlVariableCodes.ToHashSet(StringComparer.Ordinal);
        var normalized = new List<ResearchDerivedFeature>(value.DerivedFeatures.Count);
        foreach (var feature in value.DerivedFeatures)
        {
            var name = NormalizeCode(feature.Name, "派生特征名称");
            if (!availableInputs.Add(name))
                throw new ProcessResearchRuleException($"派生特征名称重复或与控制变量冲突：{name}。");
            var featureOperator = feature.Operator.Trim().ToLowerInvariant();
            if (!ResearchDerivedFeatureOperators.IsValid(featureOperator))
                throw new ProcessResearchRuleException($"派生特征 {name} 的运算符无效。");
            var inputs = feature.Inputs.Select(input =>
                NormalizeCode(input, $"派生特征 {name} 的输入")).ToArray();
            if (inputs.Length == 0)
                throw new ProcessResearchRuleException($"派生特征 {name} 至少需要一个输入。");
            var exactArity = featureOperator switch
            {
                ResearchDerivedFeatureOperators.Identity or
                    ResearchDerivedFeatureOperators.Absolute => 1,
                ResearchDerivedFeatureOperators.Difference or
                    ResearchDerivedFeatureOperators.AbsoluteDifference or
                    ResearchDerivedFeatureOperators.Ratio => 2,
                _ => 0
            };
            if (exactArity > 0 && inputs.Length != exactArity)
            {
                throw new ProcessResearchRuleException(
                    $"派生特征 {name} 的运算符 {featureOperator} 必须恰好有 {exactArity} 个输入。");
            }
            var unavailable = inputs.FirstOrDefault(input =>
                !availableInputs.Contains(input) || string.Equals(input, name, StringComparison.Ordinal));
            if (unavailable is not null)
            {
                throw new ProcessResearchRuleException(
                    $"派生特征 {name} 引用了未知或尚未定义的输入 {unavailable}。");
            }
            if (!double.IsFinite(feature.NormalizationOffset) ||
                !double.IsFinite(feature.NormalizationScale) ||
                feature.NormalizationScale <= 0 ||
                !double.IsFinite(feature.Epsilon) ||
                feature.Epsilon <= 0)
            {
                throw new ProcessResearchRuleException(
                    $"派生特征 {name} 的归一化参数或 epsilon 无效。");
            }
            normalized.Add(feature with
            {
                Name = name,
                Operator = featureOperator,
                Inputs = inputs
            });
        }

        return value with
        {
            FeatureSetId = NormalizeCode(value.FeatureSetId, "优化特征集标识"),
            DerivedFeatures = normalized
        };
    }

    private static ResearchObjective NormalizeObjective(ResearchObjective value)
    {
        if (!double.IsFinite(value.Target) || !double.IsFinite(value.Weight) || value.Weight <= 0 ||
            value.Baseline is { } baseline && !double.IsFinite(baseline) ||
            value.LowerLimit is { } lower && !double.IsFinite(lower) ||
            value.UpperLimit is { } upper && !double.IsFinite(upper) ||
            value.LowerLimit is { } min && value.UpperLimit is { } max && min >= max)
            throw new ProcessResearchRuleException("研发目标的数值范围无效。");
        var direction = value.Direction.Trim().ToLowerInvariant();
        if (direction is not ("maximize" or "minimize" or "target" or "range"))
            throw new ProcessResearchRuleException("研发目标方向必须是 maximize、minimize、target 或 range。");
        return value with
        {
            Code = NormalizeCode(value.Code, "研发目标代码"),
            Name = RequiredText(value.Name, "研发目标名称", 240),
            Unit = RequiredText(value.Unit, "研发目标单位", 40),
            Direction = direction,
            DataSource = OptionalText(value.DataSource, 500)
        };
    }

    private static ResearchVariable NormalizeVariable(ResearchVariable value)
    {
        if (!ResearchVariableRoles.IsValid(value.Role))
            throw new ProcessResearchRuleException("工艺变量角色无效。");
        if (value.LowerLimit is { } lower && !double.IsFinite(lower) ||
            value.UpperLimit is { } upper && !double.IsFinite(upper) ||
            value.LowerLimit is { } min && value.UpperLimit is { } max && min >= max)
            throw new ProcessResearchRuleException("工艺变量范围无效。");
        return value with
        {
            Code = NormalizeCode(value.Code, "工艺变量代码"),
            Name = RequiredText(value.Name, "工艺变量名称", 240),
            Role = value.Role.Trim().ToLowerInvariant(),
            Unit = RequiredText(value.Unit, "工艺变量单位", 40),
            DataSource = OptionalText(value.DataSource, 500)
        };
    }

    private static IReadOnlyList<EvidenceReference> NormalizeEvidence(
        Guid projectId,
        IReadOnlyList<EvidenceReference> source)
        => source.Select(value =>
        {
            var kind = RequiredText(value.Kind, "证据类型", 80).ToLowerInvariant();
            var hash = RequiredText(value.ContentHash, "证据内容摘要", 64).ToLowerInvariant();
            if (!EvidenceKinds.IsValid(kind))
                throw new ProcessResearchRuleException("证据类型必须是系统定义的可验证类型。");
            if (!HashPattern().IsMatch(hash))
                throw new ProcessResearchRuleException("证据内容摘要必须是 64 位 SHA-256。");
            if (value.ProjectId != Guid.Empty && value.ProjectId != projectId)
                throw new ProcessResearchRuleException("证据不属于当前研发项目。");
            return value with
            {
                EvidenceId = value.EvidenceId == Guid.Empty ? Guid.CreateVersion7() : value.EvidenceId,
                ProjectId = projectId,
                Kind = kind,
                ReferenceId = RequiredText(value.ReferenceId, "证据标识", 500),
                Summary = RequiredText(value.Summary, "证据摘要", 2000),
                ContentHash = hash,
                CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt
            };
        }).ToArray();

    private static EvidenceReference CreateEvidence(
        Guid projectId,
        string kind,
        string referenceId,
        string summary,
        string contentHash,
        DateTimeOffset createdAt)
        => new()
        {
            EvidenceId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Kind = kind,
            ReferenceId = referenceId,
            Summary = summary,
            ContentHash = contentHash.ToLowerInvariant(),
            CreatedAt = createdAt
        };

    private static IReadOnlyList<string> NormalizeCodes(
        IReadOnlyList<string> source,
        string field)
        => source.Select(value => NormalizeCode(value, field))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> NormalizeTextList(
        IReadOnlyList<string> source,
        string field,
        int maximumLength)
        => source.Select(value => RequiredText(value, field, maximumLength))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string NormalizeCode(string? value, string field)
    {
        var result = RequiredText(value, field, 120).ToLowerInvariant();
        if (!CodePattern().IsMatch(result))
            throw new ProcessResearchRuleException(
                $"{field}必须以字母开头，并且只包含小写字母、数字、点、下划线或连字符。");
        return result;
    }

    private static string NormalizeUser(string? value)
        => RequiredText(value, "用户标识", 240).ToLowerInvariant();

    private static string NormalizeStatus(
        string? value,
        Func<string?, bool> validator,
        string field)
    {
        var result = RequiredText(value, field, 80).ToLowerInvariant();
        if (!validator(result))
            throw new ProcessResearchRuleException($"{field}无效。");
        return result;
    }

    private static string RequiredText(string? value, string field, int maximumLength)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > maximumLength)
            throw new ProcessResearchRuleException($"{field}不能为空且最长 {maximumLength} 个字符。");
        return result;
    }

    private static string? OptionalText(string? value, int maximumLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result))
            return null;
        if (result.Length > maximumLength)
            throw new ProcessResearchRuleException($"文本最长 {maximumLength} 个字符。");
        return result;
    }
}
