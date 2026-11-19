// 核对实际运行参数是否执行了工程师选择；只生成证据，不改变生产状态或自动下发配方。
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
/// 按工艺变量步长、相对容差和量程下限核对实际回读值。
/// 结果只用于冻结回执和该轮验证判定；不阻止生产，也不改写工程师决定，
/// 实际运行仍按自己的实际参数参与后续模型观察。
/// </summary>
public sealed class ResearchExecutionAdherenceEvaluator(
    RecipeRecommendationBriefPolicy? briefPolicy = null)
{
    public const string PolicyVersion = "execution-adherence-v2";

    private const double RelativeTolerance = 0.01;
    private const double RangeTolerance = 1e-4;
    private const double AbsoluteTolerance = 1e-9;

    public async Task<ResearchExecutionAdherence> EvaluateAsync(
        RecipeRecommendationBrief brief,
        ResearchRecipeRecommendationDecision decision,
        ResearchRunObservation observation,
        CancellationToken ct = default)
    {
        var expected = decision.EngineerSelectedParameters.Count > 0
            ? decision.EngineerSelectedParameters
            : decision.SuggestedParameters;
        var expectedByCode = expected
            .GroupBy(static value => value.VariableCode, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var actualByCode = observation.ActualFactors
            .GroupBy(static value => value.VariableCode, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var controls = brief.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .OrderBy(static value => value.Code, StringComparer.Ordinal)
            .ToArray();
        if (controls.Length == 0 || expectedByCode.Count == 0)
            return Unverifiable("没有可核对的工程师选择参数。");
        if (briefPolicy is not null &&
            (!brief.Context.TryGetValue(RecipeRecommendationScopeKeys.ProcessSpecificationVersion, out var version) ||
             !int.TryParse(version, System.Globalization.CultureInfo.InvariantCulture, out _)))
            return Unverifiable("没有固定配方版本，无法读取参数步长并核对执行一致性。");

        IReadOnlyDictionary<string, double?> steps;
        try
        {
            steps = await ResolveStepsAsync(brief, controls, ct).ConfigureAwait(false);
        }
        catch (ProcessResearchRuleException)
        {
            return Unverifiable("无法读取冻结配方配置中的参数步长，无法核对执行一致性。");
        }
        var variables = new List<ResearchVariableAdherence>(controls.Length);
        var outside = new List<string>();
        foreach (var control in controls)
        {
            if (!expectedByCode.TryGetValue(control.Code, out var expectedSetting) ||
                !actualByCode.TryGetValue(control.Code, out var actualSetting))
                return Unverifiable($"缺少 {control.Code} 的实际参数或工程师选择，无法核对执行一致性。");
            if (!string.Equals(expectedSetting.Unit, actualSetting.Unit, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(control.Unit, actualSetting.Unit, StringComparison.OrdinalIgnoreCase))
                return Unverifiable($"变量 {control.Code} 的单位不一致，无法核对执行一致性。");

            var expectedValue = expectedSetting.Value;
            var actualValue = actualSetting.Value;
            if (!double.IsFinite(expectedValue) || !double.IsFinite(actualValue))
                return Unverifiable($"变量 {control.Code} 的数值无效，无法核对执行一致性。");

            var deviation = actualValue - expectedValue;
            var step = steps.GetValueOrDefault(control.Code);
            var tolerance = ToleranceFor(control, expectedValue, step);
            // 半步边界属于两个离散设定的中间点，不能把它归为原决定。
            var within = Math.Abs(deviation) <= tolerance + AbsoluteTolerance &&
                !(step is > 0 && double.IsFinite(step.Value) && Math.Abs(deviation) >= step.Value / 2);
            variables.Add(new ResearchVariableAdherence
            {
                VariableCode = control.Code,
                Unit = control.Unit,
                ExpectedValue = expectedValue,
                ActualValue = actualValue,
                Deviation = deviation,
                Tolerance = tolerance,
                WithinTolerance = within
            });
            if (!within) outside.Add(control.Code);
        }

        return new ResearchExecutionAdherence
        {
            Status = outside.Count == 0
                ? ResearchExecutionAdherenceStatuses.WithinTolerance
                : ResearchExecutionAdherenceStatuses.OutsideTolerance,
            PolicyVersion = PolicyVersion,
            Variables = variables,
            Reason = outside.Count == 0
                ? null
                : $"实际参数与工程师选择的偏差超出容差：{string.Join("、", outside)}。",
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<IReadOnlyDictionary<string, double?>> ResolveStepsAsync(
        RecipeRecommendationBrief brief,
        IReadOnlyList<ResearchVariable> controls,
        CancellationToken ct)
    {
        if (briefPolicy is null)
            return new Dictionary<string, double?>(StringComparer.Ordinal);
        return await briefPolicy.ResolveParameterStepsAsync(
            brief,
            controls.Select(static value => value.Code).ToArray(),
            ct).ConfigureAwait(false);
    }

    private static double ToleranceFor(
        ResearchVariable variable,
        double expectedValue,
        double? declaredStep)
    {
        var range = variable.LowerLimit is { } lower && variable.UpperLimit is { } upper
            ? Math.Abs(upper - lower)
            : 0;
        var tolerance = Math.Max(AbsoluteTolerance,
            Math.Max(Math.Abs(expectedValue) * RelativeTolerance, range * RangeTolerance));
        // 步长描述不同设定点的间隔，不是允许的执行偏差；任何其他容差也不能跨越半步。
        return declaredStep is > 0 && double.IsFinite(declaredStep.Value)
            ? Math.Min(tolerance, declaredStep.Value / 2)
            : tolerance;
    }

    private static ResearchExecutionAdherence Unverifiable(string reason)
        => new()
        {
            Status = ResearchExecutionAdherenceStatuses.Unverifiable,
            PolicyVersion = PolicyVersion,
            Variables = [],
            Reason = reason,
            CheckedAt = DateTimeOffset.UtcNow
        };
}
