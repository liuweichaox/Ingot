using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
/// Produces the user-facing, aggregate portion of experiment validation.  The
/// workflow retains its defensive validation as the final authority; this
/// service makes the same common failures visible before submission.
/// </summary>
public sealed class ResearchExperimentValidationService(IProcessResearchStore store)
    : IResearchExperimentPlanValidator
{
    public async Task<ResearchExperimentValidationResult> ValidateAsync(
        Guid projectId,
        ResearchExperiment request,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var errors = new List<ResearchExperimentValidationIssue>();
        void Add(string field, string code, string message, string? hint = null) => errors.Add(new()
        {
            Field = field,
            Code = code,
            Message = message,
            FixHint = hint
        });

        var method = request.DesignMethod?.Trim().ToLowerInvariant();
        if (!ResearchDesignMethods.IsValid(method))
            Add("designMethod", "invalid-design-method", "实验设计方法无效。", "请选择系统支持的设计方法。");
        var controlled = request.Optimization?.Mode == ResearchOptimizationModes.Controlled;
        if (request.RunPlan.Count < (controlled ? 1 : 2))
            Add("runPlan", "minimum-runs", "实验计划必须至少包含两个运行条件，不能用单点设置代替实验设计。",
                "增加一个不同的变量组合；受控在线建议例外。 ");
        if (request.RunPlan.Count > 40)
            Add("runPlan", "maximum-runs", "单批实验运行数不能超过 40。", "减少水平数、重复数或变量数。");
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var keys = request.RunPlan.Select(static run => run.ExecutionKey?.Trim() ?? "").ToArray();
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            Add("runPlan", "unique-execution-keys", "实验运行标识必须唯一且不能为空。",
                "为每条运行指定不同的 executionKey。");
        var sequences = request.RunPlan.Select((run, index) => run.Sequence > 0 ? run.Sequence : index + 1).ToArray();
        if (sequences.Distinct().Count() != sequences.Length)
            Add("runPlan", "unique-sequence", "实验执行顺序必须唯一。", "为每条运行指定不同的 sequence。");
        foreach (var run in request.RunPlan)
        {
            var codes = run.Factors.Select(static factor => factor.VariableCode?.Trim().ToLowerInvariant() ?? "").ToArray();
            if (codes.Length == 0 || codes.Any(string.IsNullOrWhiteSpace) ||
                codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
                Add("runPlan", "distinct-run-factors", "每个实验运行必须包含不重复的可控变量设置。",
                    $"检查运行 {run.ExecutionKey ?? "（未命名）"} 的变量设置。");
            foreach (var factor in run.Factors)
            {
                var code = factor.VariableCode?.Trim().ToLowerInvariant() ?? "";
                if (!controls.TryGetValue(code, out var variable))
                {
                    Add("runPlan", "unknown-control-variable", $"实验变量 {code} 不是项目中的可控变量。");
                    continue;
                }
                var normalizedValue = factor.Value;
                var convertible = string.Equals(factor.Unit?.Trim(), variable.Unit, StringComparison.OrdinalIgnoreCase) ||
                                  ResearchUnitConverter.TryConvert(factor.Value, factor.Unit, variable.Unit, out normalizedValue);
                if (!convertible)
                    Add("runPlan", "factor-unit-mismatch", $"实验变量 {code} 的单位必须与项目变量一致或可转换。",
                        $"请使用 {variable.Unit} 或可换算单位。");
                if (!double.IsFinite(normalizedValue) ||
                    variable.LowerLimit is { } low && normalizedValue < low ||
                    variable.UpperLimit is { } high && normalizedValue > high)
                    Add("runPlan", "factor-out-of-range", $"实验变量 {code} 超出允许范围。",
                        $"允许范围：{variable.LowerLimit}–{variable.UpperLimit} {variable.Unit}。");
            }
        }
        var conditions = request.RunPlan.Select(run => string.Join("|", run.Factors
                .OrderBy(static factor => factor.VariableCode, StringComparer.Ordinal)
                .Select(static factor => $"{factor.VariableCode}:{factor.Value:R}")))
            .Distinct(StringComparer.Ordinal).Count();
        if (!controlled && request.RunPlan.Count > 0 && conditions < 2)
            Add("runPlan", "distinct-conditions-required", "实验至少需要两个不同的变量组合。",
                "当前只有一个变量组合，请增加不同条件。");
        var objectives = request.ObjectiveCodes.Select(static value => value?.Trim().ToLowerInvariant() ?? "")
            .Where(static value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var knownObjectives = project.Objectives.Select(static value => value.Code).ToHashSet(StringComparer.Ordinal);
        if (objectives.Length == 0 || objectives.Any(code => !knownObjectives.Contains(code)))
            Add("objectiveCodes", "project-objective-required", "实验必须引用项目中已经定义的目标。",
                "至少选择一个项目质量目标。");
        var baseline = request.BaselineExecutionKeys.Select(static value => value?.Trim() ?? "")
            .Where(static value => value.Length > 0).ToArray();
        if (baseline.Distinct(StringComparer.Ordinal).Count() != baseline.Length)
            Add("baselineExecutionKeys", "baseline-keys-unique", "对照运行标识不能重复。", "移除重复的对照运行。");
        if (baseline.Length == 1)
            Add("baselineExecutionKeys", "baseline-minimum", "生成独立对照置信区间至少需要两个对照运行。",
                "当前 1 条，还需 1 条。");
        var baselineSet = baseline.ToHashSet(StringComparer.Ordinal);
        if (baseline.Length > 0 && keys.All(baselineSet.Contains))
            Add("baselineExecutionKeys", "non-baseline-run-required", "实验必须至少保留一个非对照运行用于效果比较。",
                "取消选择至少一条运行作为对照。");
        if (string.IsNullOrWhiteSpace(request.StopRule))
            Add("stopRule", "stop-rule-required", "停止规则未填写。", "说明触发停止的安全或质量条件。");
        if (string.IsNullOrWhiteSpace(request.RollbackPlan))
            Add("rollbackPlan", "rollback-plan-required", "回退方案未填写。", "说明停止后恢复的安全状态。");
        return new ResearchExperimentValidationResult { Errors = errors };
    }
}

public sealed class ResearchExperimentValidationException(
    IReadOnlyList<ResearchExperimentValidationIssue> errors)
    : InvalidOperationException(errors.FirstOrDefault()?.Message ?? "实验计划未通过校验。")
{
    public IReadOnlyList<ResearchExperimentValidationIssue> Errors { get; } = errors;
}
