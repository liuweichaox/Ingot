using System.Collections;
using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessConfiguration;

public sealed record ResolvedProcessAnalysis
{
    public required ProcessAnalysisPlan Plan { get; init; }
    public required ProcessDataModel DataModel { get; init; }
}

/// <summary>
/// Resolves the published process-analysis configuration that applies to an immutable
/// production context. This is the single runtime entry point for stages and signals.
/// </summary>
public sealed class ProcessAnalysisResolver(IProcessConfigurationStore store)
{
    public async Task<ResolvedProcessAnalysis?> ResolveAsync(
        IReadOnlyDictionary<string, string> context,
        string analysisScope,
        CancellationToken ct = default)
    {
        var result = await ResolveManyAsync([context], analysisScope, ct).ConfigureAwait(false);
        return result[0];
    }

    public async Task<IReadOnlyList<ResolvedProcessAnalysis?>> ResolveManyAsync(
        IReadOnlyList<IReadOnlyDictionary<string, string>> contexts,
        string analysisScope,
        CancellationToken ct = default)
    {
        if (contexts.Count == 0)
            return [];
        var plans = await store.ListAnalysisPlansAsync(ct).ConfigureAwait(false);
        var recipeCache = new Dictionary<(string Id, int Version), RecipeVersion?>();
        var modelCache = new Dictionary<(string Id, int Version), ProcessDataModel?>();
        var result = new List<ResolvedProcessAnalysis?>(contexts.Count);
        foreach (var context in contexts)
        {
            var modelId = ContextValue(context, "data_model_id")?.Trim().ToLowerInvariant();
            var hasModelVersion = int.TryParse(ContextValue(context, "data_model_version"), out var modelVersion)
                                  && modelVersion > 0;
            if (string.IsNullOrWhiteSpace(modelId) || !hasModelVersion)
            {
                var recipeId = ContextValue(context, "recipe_id")?.Trim().ToLowerInvariant();
                var hasRecipeVersion = int.TryParse(ContextValue(context, "recipe_version"), out var recipeVersion)
                                       && recipeVersion > 0;
                RecipeVersion? recipe = null;
                if (!string.IsNullOrWhiteSpace(recipeId) && hasRecipeVersion)
                {
                    var recipeKey = (recipeId, recipeVersion);
                    if (!recipeCache.TryGetValue(recipeKey, out recipe))
                    {
                        recipe = await store.GetRecipeVersionAsync(recipeId, recipeVersion, ct).ConfigureAwait(false);
                        recipeCache[recipeKey] = recipe;
                    }
                }
                modelId = recipe?.DataModelId;
                modelVersion = recipe?.DataModelVersion ?? 0;
            }

            var plan = plans
                .Where(static item => item.Status == ConfigurationStatuses.Published)
                .Where(item => string.Equals(item.AnalysisScope, analysisScope, StringComparison.Ordinal))
                // An empty selector means "all contexts of this data model", never "all industries".
                // When the model cannot be inferred, only an explicit selector may select a plan.
                .Where(item => !string.IsNullOrWhiteSpace(modelId)
                    ? string.Equals(item.DataModelId, modelId, StringComparison.Ordinal) &&
                      item.DataModelVersion == modelVersion
                    : item.ContextSelector.Count > 0)
                .Where(item => MatchesSelector(item.ContextSelector, context))
                .OrderByDescending(static item => item.ContextSelector.Count)
                .ThenByDescending(static item => item.Version)
                .ThenByDescending(static item => item.UpdatedAt)
                .FirstOrDefault();
            if (plan is null)
            {
                result.Add(null);
                continue;
            }

            var modelKey = (plan.DataModelId, plan.DataModelVersion);
            if (!modelCache.TryGetValue(modelKey, out var model))
            {
                model = await store.GetDataModelAsync(plan.DataModelId, plan.DataModelVersion, ct)
                    .ConfigureAwait(false);
                modelCache[modelKey] = model;
            }
            result.Add(model is null || model.Status != ConfigurationStatuses.Published
                ? null
                : new ResolvedProcessAnalysis { Plan = plan, DataModel = model });
        }
        return result;
    }

    public async Task<RecipeVersion?> ResolveRecipeAsync(
        IReadOnlyDictionary<string, string> context,
        CancellationToken ct = default)
    {
        var values = await ResolveRecipesAsync([context], ct).ConfigureAwait(false);
        return values[0];
    }

    public async Task<IReadOnlyList<RecipeVersion?>> ResolveRecipesAsync(
        IReadOnlyList<IReadOnlyDictionary<string, string>> contexts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        var cache = new Dictionary<(string Id, int Version), RecipeVersion?>();
        var result = new List<RecipeVersion?>(contexts.Count);
        foreach (var context in contexts)
        {
            ct.ThrowIfCancellationRequested();
        var recipeId = ContextValue(context, "recipe_id");
        var versionText = ContextValue(context, "recipe_version");
        if (string.IsNullOrWhiteSpace(recipeId) ||
            !int.TryParse(versionText, out var version) ||
            version < 1)
        {
                result.Add(null);
                continue;
        }

            var key = (recipeId.Trim().ToLowerInvariant(), version);
            if (!cache.TryGetValue(key, out var recipe))
            {
                recipe = await store.GetRecipeVersionAsync(key.Item1, key.version, ct)
                    .ConfigureAwait(false);
                cache[key] = recipe;
            }
            result.Add(recipe);
        }
        return result;
    }

    public static bool MatchesSelector(
        IReadOnlyDictionary<string, string> selector,
        IReadOnlyDictionary<string, string> context)
        => selector.All(pair => string.Equals(
            ContextValue(context, pair.Key),
            pair.Value,
            StringComparison.OrdinalIgnoreCase));

    public static string? ResolveStage(
        IReadOnlyDictionary<string, string> context,
        IReadOnlyDictionary<string, object?> data,
        ProcessDataModel model)
    {
        var stageNumberItem = model.Acquisition.DataItems
            .SingleOrDefault(static item => item.Category == "stage");
        if (stageNumberItem is null ||
            !TryReadInteger(data, stageNumberItem.Code, out var stageNumber))
            return null;
        return stageNumber.ToString(CultureInfo.InvariantCulture);
    }

    public static string? ContextValue(IReadOnlyDictionary<string, string> context, string key)
    {
        if (context.TryGetValue(key, out var value))
            return value;
        var underscore = key.Replace('.', '_');
        if (context.TryGetValue(underscore, out value))
            return value;
        var dotted = key.Replace('_', '.');
        return context.TryGetValue(dotted, out value) ? value : null;
    }

    private static bool TryReadInteger(
        IReadOnlyDictionary<string, object?> data,
        string key,
        out long value)
    {
        value = default;
        if (!data.TryGetValue("values", out var container))
            return false;
        object? raw = null;
        if (container is JsonElement { ValueKind: JsonValueKind.Object } element &&
            element.TryGetProperty(key, out var property))
        {
            raw = property;
        }
        else if (container is IReadOnlyDictionary<string, object?> readOnly &&
                 readOnly.TryGetValue(key, out var readOnlyValue))
        {
            raw = readOnlyValue;
        }
        else if (container is IDictionary dictionary && dictionary.Contains(key))
        {
            raw = dictionary[key];
        }
        if (raw is JsonElement json)
            return json.ValueKind == JsonValueKind.Number && json.TryGetInt64(out value);
        try
        {
            value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return raw is not null;
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }
}
