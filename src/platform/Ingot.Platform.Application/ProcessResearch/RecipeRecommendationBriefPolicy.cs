// 规范化、校验并冻结下一配方建议的生成条件；条件随建议一起保存，之后不可修改。
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
/// 建议条件只能引用已发布配方版本的数据模型中允许调整的参数，并在生成时冻结上下文策略。
/// </summary>
public sealed partial class RecipeRecommendationBriefPolicy(
    IProcessConfigurationStore? processConfigurations = null)
{
    private const string ControlParameterSourcePrefix = "control-parameter:";

    public async Task<RecipeRecommendationBrief> ResolveAsync(
        RecipeRecommendationBrief draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var brief = Normalize(draft);
        if (processConfigurations is null)
            return Canonicalize(brief);
        await ValidateAgainstPublishedRecipeAsync(brief, ct).ConfigureAwait(false);
        return Canonicalize(brief with
        {
            Context = await FreezeContextPolicyAsync(brief.Context, ct).ConfigureAwait(false)
        });
    }

    public static string Hash(RecipeRecommendationBrief brief)
        => Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(Canonicalize(brief))));

    /// <summary>机理知识和机理模型的适用范围只匹配这组由建议条件派生的维度。</summary>
    internal static IReadOnlyDictionary<string, string> ApplicabilityContext(RecipeRecommendationBrief brief)
    {
        var context = new Dictionary<string, string>(brief.Context, StringComparer.OrdinalIgnoreCase)
        {
            ["site"] = brief.SiteCode,
            ["site-code"] = brief.SiteCode,
            ["process-specification"] = brief.ProcessSpecificationId
        };
        if (brief.Context.TryGetValue(RecipeRecommendationScopeKeys.ProductCode, out var product))
            context["product"] = product;
        if (brief.Context.TryGetValue(RecipeRecommendationScopeKeys.EquipmentId, out var equipment))
            context["equipment"] = equipment;
        return context;
    }

    internal static RecipeRecommendationBrief Canonicalize(RecipeRecommendationBrief brief)
        => brief with
        {
            Objectives = brief.Objectives.OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            Variables = brief.Variables.OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            Constraints = brief.Constraints.OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            OutcomeConstraints = brief.OutcomeConstraints
                .OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            OptimizationFeatures = brief.OptimizationFeatures with
            {
                DerivedFeatures = brief.OptimizationFeatures.DerivedFeatures
                    .OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray()
            },
            Context = brief.Context.OrderBy(static value => value.Key, StringComparer.Ordinal)
                .ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal)
        };

    internal static RecipeRecommendationBrief Normalize(RecipeRecommendationBrief value)
    {
        var siteCode = RequiredText(value.SiteCode, "站点", 120);
        var processSpecificationId = RequiredText(value.ProcessSpecificationId, "配方", 120);
        var objectives = value.Objectives.Select(NormalizeObjective).ToArray();
        if (objectives.Length == 0)
            throw new ProcessResearchRuleException("建议条件至少需要一个质量目标。");
        RequireDistinct(objectives.Select(static item => item.Code), "质量目标代码不能重复。");
        var variables = value.Variables.Select(NormalizeVariable).ToArray();
        RequireDistinct(variables.Select(static item => item.Code), "工艺变量代码不能重复。");
        var controls = variables.Where(static item => item.Role == ResearchVariableRoles.Control).ToArray();
        if (controls.Length == 0 ||
            controls.Any(static item => item.LowerLimit is null || item.UpperLimit is null))
            throw new ProcessResearchRuleException("建议条件至少需要一个可调参数，且每个可调参数都要有上下界。");
        var knownVariables = variables.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var constraints = value.Constraints.Select(item => NormalizeConstraint(item, knownVariables)).ToArray();
        RequireDistinct(constraints.Select(static item => item.Code), "参数约束代码不能重复。");
        var outcomeConstraints = value.OutcomeConstraints.Select(NormalizeOutcomeConstraint).ToArray();
        RequireDistinct(outcomeConstraints.Select(static item => item.Code), "结果约束代码不能重复。");
        if (outcomeConstraints.Select(static item => item.Code)
            .Intersect(objectives.Select(static item => item.Code), StringComparer.Ordinal).Any())
            throw new ProcessResearchRuleException("质量目标代码与结果约束代码不能重复。");

        return value with
        {
            SiteCode = siteCode,
            ProcessSpecificationId = processSpecificationId,
            Name = OptionalText(value.Name, 240) ?? processSpecificationId,
            Objectives = objectives,
            Variables = variables,
            Constraints = constraints,
            OutcomeConstraints = outcomeConstraints,
            OptimizationFeatures = NormalizeOptimizationFeatures(
                value.OptimizationFeatures, controls.Select(static item => item.Code)),
            Context = NormalizeContext(value.Context, processSpecificationId)
        };
    }

    public async Task<IReadOnlyDictionary<string, double>> SnapRecommendedParametersAsync(
        RecipeRecommendationBrief brief,
        IReadOnlyDictionary<string, double> parameters,
        CancellationToken ct = default)
    {
        if (processConfigurations is null || parameters.Count == 0)
            return parameters;
        var model = await LoadPublishedModelAsync(brief, ct).ConfigureAwait(false);
        var definitions = model.ControlParameters.ToDictionary(
            static value => value.Code, StringComparer.OrdinalIgnoreCase);
        var controls = brief.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var snapped = new Dictionary<string, double>(parameters, StringComparer.Ordinal);
        foreach (var (code, value) in parameters)
        {
            if (!controls.TryGetValue(code, out var variable) ||
                variable.LowerLimit is not { } lower ||
                variable.UpperLimit is not { } upper)
                continue;
            var parameterCode = variable.DataSource is null
                ? variable.Code
                : variable.DataSource.StartsWith(ControlParameterSourcePrefix, StringComparison.OrdinalIgnoreCase)
                    ? variable.DataSource[ControlParameterSourcePrefix.Length..]
                    : variable.Code;
            if (!definitions.TryGetValue(parameterCode.Trim(), out var definition) || definition.Step is not { } step)
                continue;
            var origin = definition.Minimum ?? lower;
            if (!TrySnapToStep(value, origin, step, lower, upper, out var aligned))
                throw new ProcessResearchRuleException($"推荐参数 {code} 按步长对齐后超出允许范围。");
            snapped[code] = aligned;
        }
        return snapped;
    }

    internal static bool TrySnapToStep(
        double value,
        double origin,
        double step,
        double lower,
        double upper,
        out double snapped)
    {
        snapped = value;
        if (!(step > 0) || !double.IsFinite(value) || !double.IsFinite(origin) || !double.IsFinite(step))
            return true;
        var stepDecimal = (decimal)step;
        var aligned = (decimal)origin +
            decimal.Round(((decimal)value - (decimal)origin) / stepDecimal, 0, MidpointRounding.AwayFromZero) * stepDecimal;
        if (aligned < (decimal)lower)
            aligned += stepDecimal;
        if (aligned > (decimal)upper)
            aligned -= stepDecimal;
        if (aligned < (decimal)lower || aligned > (decimal)upper)
            return false;
        snapped = (double)aligned;
        return true;
    }

    private async Task<ProcessDataModel> LoadPublishedModelAsync(
        RecipeRecommendationBrief brief,
        CancellationToken ct)
    {
        var specifications = (await processConfigurations!.ListProcessSpecificationsAsync(ct).ConfigureAwait(false))
            .Where(value => string.Equals(
                value.ProcessSpecificationId, brief.ProcessSpecificationId, StringComparison.Ordinal) &&
                value.Status != ConfigurationStatuses.Draft)
            .ToArray();
        var specification = brief.Context.TryGetValue(
                RecipeRecommendationScopeKeys.ProcessSpecificationVersion, out var pinned)
            ? specifications.FirstOrDefault(value => value.Version == int.Parse(pinned, System.Globalization.CultureInfo.InvariantCulture))
            : specifications.MaxBy(static value => value.Version);
        if (specification is null)
            throw new ProcessResearchRuleException($"配方 {brief.ProcessSpecificationId} 没有可用的已发布版本。");
        return await processConfigurations.GetDataModelAsync(
                specification.DataModelId, specification.DataModelVersion, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException(
                $"配方 {brief.ProcessSpecificationId} 引用的数据模型不存在。");
    }

    private async Task ValidateAgainstPublishedRecipeAsync(
        RecipeRecommendationBrief brief,
        CancellationToken ct)
    {
        var model = await LoadPublishedModelAsync(brief, ct).ConfigureAwait(false);
        var parameters = model.ControlParameters.ToDictionary(
            static value => value.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var variable in brief.Variables.Where(static item => item.Role == ResearchVariableRoles.Control))
        {
            var parameterCode = variable.DataSource is null
                ? variable.Code
                : variable.DataSource.StartsWith(ControlParameterSourcePrefix, StringComparison.OrdinalIgnoreCase)
                    ? variable.DataSource[ControlParameterSourcePrefix.Length..].Trim()
                    : throw new ProcessResearchRuleException(
                        $"可调参数 {variable.Code} 只能来自配方参数（{ControlParameterSourcePrefix}）。");
            if (!parameters.TryGetValue(parameterCode, out var parameter))
                throw new ProcessResearchRuleException(
                    $"可调参数 {variable.Code} 不在配方 {brief.ProcessSpecificationId} 的参数定义中。");
            if (!parameter.ChangeAllowed)
                throw new ProcessResearchRuleException($"配方参数 {parameter.Code} 不允许调整。");
            if (parameter.DataType is not ("double" or "integer" or "int" or "decimal" or "number"))
                throw new ProcessResearchRuleException($"配方参数 {parameter.Code} 不是数值参数。");
            if (!string.IsNullOrWhiteSpace(parameter.Unit) &&
                !string.Equals(parameter.Unit, variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException(
                    $"可调参数 {variable.Code} 的单位必须与配方参数一致（{parameter.Unit}）。");
            if (parameter.Minimum is { } minimum && variable.LowerLimit < minimum ||
                parameter.Maximum is { } maximum && variable.UpperLimit > maximum)
                throw new ProcessResearchRuleException(
                    $"可调参数 {variable.Code} 的范围超出配方参数允许范围。");
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> FreezeContextPolicyAsync(
        IReadOnlyDictionary<string, string> context,
        CancellationToken ct)
    {
        if (!ResearchContextAdmissionEvaluator.TryParseScenarioPackageReference(
                context,
                out var packageId,
                out var version))
            return context;
        var package = await processConfigurations!.GetScenarioPackageAsync(packageId, version, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException($"建议条件引用的工艺配置不存在：{packageId} v{version}。");
        if (package.Status == ConfigurationStatuses.Draft)
            throw new ProcessResearchRuleException("建议条件必须引用已发布的工艺配置版本。");
        return new Dictionary<string, string>(context, StringComparer.Ordinal)
        {
            [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                $"{package.PackageId}:{package.Version}",
            [ResearchContextAdmissionEvaluator.PolicyHashContextKey] =
                ResearchContextAdmissionEvaluator.ComputePolicyHash(package)
        };
    }

    private static Dictionary<string, string> NormalizeContext(
        IReadOnlyDictionary<string, string> source,
        string processSpecificationId)
    {
        var context = source
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) &&
                                  !string.IsNullOrWhiteSpace(pair.Value) &&
                                  !string.Equals(
                                      pair.Key.Trim(),
                                      ResearchContextAdmissionEvaluator.PolicyHashContextKey,
                                      StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key.Trim().ToLowerInvariant(),
                static pair => pair.Value.Trim(),
                StringComparer.Ordinal);
        if (context.TryGetValue(RecipeRecommendationScopeKeys.ProcessSpecificationId, out var scoped) &&
            !string.Equals(scoped, processSpecificationId, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("建议条件中的配方与上下文范围不一致。");
        context[RecipeRecommendationScopeKeys.ProcessSpecificationId] = processSpecificationId;
        if (context.TryGetValue(RecipeRecommendationScopeKeys.ProcessSpecificationVersion, out var version) &&
            (!int.TryParse(version, out var parsedVersion) || parsedVersion < 1))
            throw new ProcessResearchRuleException("配方版本号必须是正整数。");
        if (context.TryGetValue(RecipeRecommendationScopeKeys.LookbackDays, out var lookback) &&
            (!int.TryParse(lookback, out var parsedLookback) || parsedLookback is < 1 or > 3650))
            throw new ProcessResearchRuleException("历史数据窗口必须是 1 到 3650 天。");
        return context;
    }

    private static ResearchConstraint NormalizeConstraint(
        ResearchConstraint item,
        IReadOnlyDictionary<string, ResearchVariable> knownVariables)
    {
        var variableCode = NormalizeCode(item.VariableCode, "约束变量");
        if (!knownVariables.TryGetValue(variableCode, out var variable))
            throw new ProcessResearchRuleException($"约束引用了未定义变量 {variableCode}。");
        if (variable.Role != ResearchVariableRoles.Control)
            throw new ProcessResearchRuleException($"参数约束 {item.Code} 必须引用可调参数。");
        if (!double.IsFinite(item.Limit))
            throw new ProcessResearchRuleException("约束限值必须是有限数值。");
        var constraintOperator = item.Operator.Trim();
        if (constraintOperator is not ("<=" or ">="))
            throw new ProcessResearchRuleException("参数约束操作符必须是 <= 或 >=。");
        var unit = RequiredText(item.Unit, "约束单位", 40);
        var limit = item.Limit;
        if (!string.Equals(unit, variable.Unit, StringComparison.OrdinalIgnoreCase) &&
            !ProcessUnitConverter.TryConvert(item.Limit, unit, variable.Unit, out limit))
            throw new ProcessResearchRuleException(
                $"参数约束 {item.Code} 的单位必须与变量一致或可转换为 {variable.Unit}。");
        return item with
        {
            Code = NormalizeCode(item.Code, "约束代码"),
            Description = RequiredText(item.Description, "约束说明", 1000),
            VariableCode = variableCode,
            Operator = constraintOperator,
            Limit = limit,
            Unit = variable.Unit
        };
    }

    private static ResearchOutcomeConstraint NormalizeOutcomeConstraint(ResearchOutcomeConstraint item)
    {
        if (!double.IsFinite(item.Limit) ||
            !double.IsFinite(item.MinimumProbability) ||
            item.MinimumProbability is <= 0 or > 1)
            throw new ProcessResearchRuleException("结果约束限值或最低可行概率无效。");
        var constraintOperator = item.Operator.Trim();
        if (constraintOperator is not ("<=" or ">="))
            throw new ProcessResearchRuleException("结果约束操作符必须是 <= 或 >=。");
        return item with
        {
            Code = NormalizeCode(item.Code, "结果约束代码"),
            Description = RequiredText(item.Description, "结果约束说明", 1000),
            OutcomeCode = NormalizeCode(item.OutcomeCode, "结果约束指标"),
            Operator = constraintOperator,
            Unit = RequiredText(item.Unit, "结果约束单位", 40),
            DataSource = OptionalText(item.DataSource, 500)
        };
    }

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
            throw new ProcessResearchRuleException("质量目标的数值范围无效。");
        var direction = value.Direction.Trim().ToLowerInvariant();
        if (direction is not ("maximize" or "minimize" or "target" or "range"))
            throw new ProcessResearchRuleException("质量目标方向必须是 maximize、minimize、target 或 range。");
        return value with
        {
            Code = NormalizeCode(value.Code, "质量目标代码"),
            Name = RequiredText(value.Name, "质量目标名称", 240),
            Unit = RequiredText(value.Unit, "质量目标单位", 40),
            Direction = direction,
            DataSource = OptionalText(value.DataSource, 500)
        };
    }

    private static ResearchVariable NormalizeVariable(ResearchVariable value)
    {
        var role = value.Role.Trim().ToLowerInvariant();
        if (!ResearchVariableRoles.IsValid(role))
            throw new ProcessResearchRuleException("工艺变量角色无效。");
        if (value.LowerLimit is { } lower && !double.IsFinite(lower) ||
            value.UpperLimit is { } upper && !double.IsFinite(upper) ||
            value.LowerLimit is { } min && value.UpperLimit is { } max && min >= max)
            throw new ProcessResearchRuleException("工艺变量范围无效。");
        return value with
        {
            Code = NormalizeCode(value.Code, "工艺变量代码"),
            Name = RequiredText(value.Name, "工艺变量名称", 240),
            Role = role,
            Unit = RequiredText(value.Unit, "工艺变量单位", 40),
            DataSource = OptionalText(value.DataSource, 500)
        };
    }

    private static void RequireDistinct(IEnumerable<string> codes, string message)
    {
        var values = codes.ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ProcessResearchRuleException(message);
    }

    private static string NormalizeCode(string? value, string field)
    {
        var result = RequiredText(value, field, 120).ToLowerInvariant();
        if (!CodePattern().IsMatch(result))
            throw new ProcessResearchRuleException(
                $"{field}必须以字母开头，并且只包含小写字母、数字、点、下划线或连字符。");
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

    [GeneratedRegex("^[a-z][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
