using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;

namespace Ingot.Platform.Application.ProcessResearch;

internal sealed record AppliedMechanismKnowledge(
    IReadOnlyList<MechanismClaimVersion> Claims,
    IReadOnlyList<MechanismClaimConstraint> HardConstraints,
    IReadOnlyList<MechanismClaimConstraint> RankingConstraints);

internal static class MechanismKnowledgeExperimentPolicy
{
    public static string SnapshotHash(AppliedMechanismKnowledge knowledge)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            knowledge.Claims
                .OrderBy(static value => value.ClaimId)
                .Select(static value => new { value.ClaimId, value.Version, value.ContentHash }))));

    public static AppliedMechanismKnowledge Select(
        ResearchProject project,
        IReadOnlyList<MechanismClaimVersion> claims,
        IReadOnlyList<MechanismClaimConflict> conflicts)
    {
        var conflicted = conflicts
            .Where(static value => value.Status == "open")
            .SelectMany(static value => new[] { value.LeftClaimId, value.RightClaimId })
            .ToHashSet();
        var context = BuildContext(project);
        var selected = claims
            .Where(value => value.ProjectId == project.ProjectId)
            .Where(value => value.Status == MechanismClaimStatuses.Active)
            .Where(value => !conflicted.Contains(value.ClaimId))
            .Where(value => value.Applicability.Count > 0 && value.Applicability.All(scope =>
                context.TryGetValue(scope.DimensionCode, out var actual) &&
                string.Equals(actual, scope.DimensionValue, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static value => value.ClaimId)
            .ToArray();
        var constraints = selected.SelectMany(static value => value.Constraints).ToArray();
        return new AppliedMechanismKnowledge(
            selected,
            constraints.Where(static value => value.Severity == "hard").ToArray(),
            constraints.Where(static value => value.Severity == "soft").ToArray());
    }

    public static OptimizerCampaignInput ApplyHardConstraints(
        OptimizerCampaignInput campaign,
        AppliedMechanismKnowledge knowledge)
    {
        if (knowledge.HardConstraints.Count == 0) return campaign;
        var variables = campaign.Variables.ToDictionary(static value => value.Name, StringComparer.Ordinal);
        foreach (var constraint in knowledge.HardConstraints)
        {
            if (!variables.TryGetValue(constraint.VariableCode, out var variable))
                throw new ProcessResearchRuleException(
                    $"生效机理约束引用了非可控变量 {constraint.VariableCode}。");
            if (!string.Equals(
                    ResearchUnitConverter.NormalizeCode(variable.Unit),
                    ResearchUnitConverter.NormalizeCode(constraint.Unit),
                    StringComparison.Ordinal))
                throw new ProcessResearchRuleException(
                    $"生效机理约束 {constraint.VariableCode} 的单位与项目变量不一致。");
            var lower = Math.Max(variable.Low, constraint.Minimum ?? variable.Low);
            var upper = Math.Min(variable.High, constraint.Maximum ?? variable.High);
            if (lower > upper)
                throw new ProcessResearchRuleException(
                    $"生效机理约束使变量 {constraint.VariableCode} 不存在可行范围。");
            variables[constraint.VariableCode] = variable with { Low = lower, High = upper };
        }
        return campaign with
        {
            Variables = campaign.Variables.Select(value => variables[value.Name]).ToArray()
        };
    }

    public static IReadOnlyList<OptimizerSuggestionOutput> Rank(
        IReadOnlyList<OptimizerSuggestionOutput> suggestions,
        AppliedMechanismKnowledge knowledge,
        IReadOnlyDictionary<string, ResearchVariable> controls)
    {
        if (knowledge.RankingConstraints.Count == 0) return suggestions;
        var constraints = knowledge.RankingConstraints
            .DistinctBy(static value => (
                value.VariableCode, value.ConstraintKind, value.Minimum, value.Maximum, value.Unit))
            .ToArray();
        var finiteAcquisitions = suggestions
            .Select(static value => value.AcquisitionValue)
            .Where(static value => value is not null && double.IsFinite(value.Value))
            .Select(static value => value!.Value)
            .ToArray();
        var minimumAcquisition = finiteAcquisitions.Length == 0 ? 0 : finiteAcquisitions.Min();
        var acquisitionWidth = finiteAcquisitions.Length == 0
            ? 1
            : Math.Max(finiteAcquisitions.Max() - minimumAcquisition, 1e-12);
        return suggestions.Select((value, index) => new
            {
                Value = value,
                Index = index,
                Penalty = constraints.Average(constraint =>
                    Math.Min(SoftPenalty(value, constraint, controls), 1)),
                Acquisition = value.AcquisitionValue is { } acquisition && double.IsFinite(acquisition)
                    ? (acquisition - minimumAcquisition) / acquisitionWidth
                    : 0
            })
            .OrderByDescending(static value => 0.75 * value.Acquisition - 0.25 * value.Penalty)
            .ThenByDescending(static value => value.Value.AcquisitionValue ?? double.NegativeInfinity)
            .ThenBy(static value => value.Index)
            .Select(static value => value.Value)
            .ToArray();
    }

    public static void ValidateHardConstraints(
        OptimizerSuggestionOutput suggestion,
        AppliedMechanismKnowledge knowledge)
    {
        foreach (var constraint in knowledge.HardConstraints)
        {
            if (!suggestion.RecommendedParameters.TryGetValue(constraint.VariableCode, out var value) ||
                constraint.Minimum is { } minimum && value < minimum ||
                constraint.Maximum is { } maximum && value > maximum)
            {
                throw new ProcessResearchRuleException(
                    $"优化建议违反生效机理硬约束：{constraint.VariableCode}。");
            }
        }
    }

    public static void ValidateHardConstraints(
        ResearchExperiment experiment,
        AppliedMechanismKnowledge knowledge)
    {
        foreach (var run in experiment.RunPlan)
        {
            ValidateHardConstraints(new OptimizerSuggestionOutput
            {
                RecommendedParameters = run.Factors.ToDictionary(
                    static value => value.VariableCode,
                    static value => value.Value,
                    StringComparer.Ordinal)
            }, knowledge);
        }
    }

    private static double SoftPenalty(
        OptimizerSuggestionOutput suggestion,
        MechanismClaimConstraint constraint,
        IReadOnlyDictionary<string, ResearchVariable> controls)
    {
        if (!suggestion.RecommendedParameters.TryGetValue(constraint.VariableCode, out var value) ||
            !controls.TryGetValue(constraint.VariableCode, out var variable) ||
            variable.LowerLimit is not { } projectLower || variable.UpperLimit is not { } projectUpper)
            return 1;
        var width = Math.Max(projectUpper - projectLower, 1e-12);
        if (constraint.Minimum is { } minimum && value < minimum)
            return (minimum - value) / width;
        if (constraint.Maximum is { } maximum && value > maximum)
            return (value - maximum) / width;
        return 0;
    }

    private static Dictionary<string, string> BuildContext(ResearchProject project)
    {
        var context = new Dictionary<string, string>(project.Context, StringComparer.OrdinalIgnoreCase)
        {
            ["project-code"] = project.Code,
            ["process-name"] = project.ProcessName,
            ["process"] = project.ProcessName
        };
        Add(context, "product-name", project.ProductName);
        Add(context, "product", project.ProductName);
        Add(context, "material-name", project.MaterialName);
        Add(context, "material", project.MaterialName);
        Add(context, "site-code", project.SiteCode);
        Add(context, "site", project.SiteCode);
        return context;
    }

    private static void Add(IDictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
    }
}
