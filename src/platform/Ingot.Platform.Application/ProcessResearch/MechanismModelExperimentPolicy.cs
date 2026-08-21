
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

internal sealed record AppliedMechanismModels(
    IReadOnlyList<MechanismModelApplicationReference> References,
    IReadOnlyList<OptimizerDerivedFeatureInput> DerivedFeatures)
{
    public string SnapshotHash => References.Count == 0
        ? "none"
        : Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            References.OrderBy(static value => value.FusionId, StringComparer.Ordinal))));
}

internal static class MechanismModelExperimentPolicy
{
    public static AppliedMechanismModels Select(
        ResearchProject project,
        IReadOnlyList<MechanismModelVersion> models,
        IReadOnlyList<MechanismFusionDefinition> fusions)
    {
        var context = BuildContext(project);
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control &&
                value.LowerLimit is not null && value.UpperLimit is not null)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var activeModels = models
            .Where(static value => value.Status == MechanismModelStatuses.Active)
            .ToDictionary(static value => (value.ModelId, value.Version));
        var reservedFeatureNames = project.OptimizationFeatures.DerivedFeatures
            .Select(static value => value.Name)
            .Concat(controls.Keys)
            .ToHashSet(StringComparer.Ordinal);
        var objectiveCodes = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var references = new List<MechanismModelApplicationReference>();
        var features = new List<OptimizerDerivedFeatureInput>();
        foreach (var fusion in fusions
            .Where(static value => value.Status == MechanismModelStatuses.Active &&
                value.Mode == MechanismFusionModes.MechanismAsFeature)
            .Where(value => Matches(value.ApplicabilityContext, context))
            .OrderBy(static value => value.FusionId, StringComparer.Ordinal))
        {
            if (!activeModels.TryGetValue(
                    (fusion.MechanismModelId, fusion.MechanismModelVersion), out var model) ||
                !Matches(model.ApplicabilityContext, context))
                continue;
            if (model.EquationKind != "affine" || model.Inputs.Count == 0 ||
                model.Inputs.Any(value => !controls.ContainsKey(value.Code)) ||
                model.Inputs.Any(value => !model.Coefficients.ContainsKey(value.Code)) ||
                model.Inputs.Any(value => !string.Equals(
                    ProcessUnitConverter.NormalizeCode(value.Unit),
                    ProcessUnitConverter.NormalizeCode(controls[value.Code].Unit),
                    StringComparison.Ordinal)) ||
                !objectiveCodes.Contains(fusion.OutputCode))
                continue;
            if (reservedFeatureNames.Contains(fusion.MechanismFeatureCode) ||
                features.Any(value => string.Equals(
                    value.Name, fusion.MechanismFeatureCode, StringComparison.Ordinal)))
                throw new ProcessResearchRuleException(
                    $"多个生效机理融合定义使用相同特征代码 {fusion.MechanismFeatureCode}。");

            var (offset, scale) = Normalization(model, controls);
            features.Add(new OptimizerDerivedFeatureInput
            {
                Name = fusion.MechanismFeatureCode,
                Operator = "affine",
                Inputs = model.Inputs.Select(static value => value.Code).ToArray(),
                Intercept = model.Intercept,
                Coefficients = model.Inputs.Select(value => model.Coefficients[value.Code]).ToArray(),
                NormalizationOffset = offset,
                NormalizationScale = scale
            });
            references.Add(new MechanismModelApplicationReference
            {
                FusionId = fusion.FusionId,
                FusionVersion = fusion.Version,
                FusionHash = fusion.ContentHash,
                MechanismModelId = model.ModelId,
                MechanismModelVersion = model.Version,
                MechanismModelHash = model.ContentHash,
                FeatureCode = fusion.MechanismFeatureCode
            });
        }
        return new AppliedMechanismModels(references, features);
    }

    public static OptimizerCampaignInput Apply(
        OptimizerCampaignInput campaign,
        AppliedMechanismModels models)
        => models.DerivedFeatures.Count == 0
            ? campaign
            : campaign with
            {
                DerivedFeatures = campaign.DerivedFeatures.Concat(models.DerivedFeatures).ToArray()
            };

    private static (double Offset, double Scale) Normalization(
        MechanismModelVersion model,
        IReadOnlyDictionary<string, ResearchVariable> controls)
    {
        if (model.Output.ValidMinimum is { } outputMinimum &&
            model.Output.ValidMaximum is { } outputMaximum && outputMaximum > outputMinimum)
            return (outputMinimum, outputMaximum - outputMinimum);
        var low = model.Intercept;
        var high = model.Intercept;
        foreach (var input in model.Inputs)
        {
            var variable = controls[input.Code];
            var coefficient = model.Coefficients[input.Code];
            var first = coefficient * variable.LowerLimit!.Value;
            var second = coefficient * variable.UpperLimit!.Value;
            low += Math.Min(first, second);
            high += Math.Max(first, second);
        }
        return (low, Math.Max(high - low, 1e-12));
    }

    private static bool Matches(
        IReadOnlyDictionary<string, string> required,
        IReadOnlyDictionary<string, string> actual)
        => required.Count > 0 && required.All(value =>
            actual.TryGetValue(value.Key, out var actualValue) &&
            string.Equals(actualValue, value.Value, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> BuildContext(ResearchProject project)
    {
        var context = new Dictionary<string, string>(project.Context, StringComparer.OrdinalIgnoreCase)
        {
            ["project-code"] = project.Code,
            ["process"] = project.ProcessName
        };
        Add(context, "product", project.ProductName);
        Add(context, "material", project.MaterialName);
        Add(context, "site", project.SiteCode);
        return context;
    }

    private static void Add(IDictionary<string, string> context, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) context[key] = value;
    }
}
