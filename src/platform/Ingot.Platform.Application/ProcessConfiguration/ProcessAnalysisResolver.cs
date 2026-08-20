using Ingot.Platform.Application.ProcessConfiguration;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessConfiguration;

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
        var processSpecificationCache = new Dictionary<(string Id, int Version), ProcessSpecification?>();
        var modelCache = new Dictionary<(string Id, int Version), ProcessDataModel?>();
        var result = new List<ResolvedProcessAnalysis?>(contexts.Count);
        foreach (var context in contexts)
        {
            var modelId = ContextValue(context, "data_model_id")?.Trim();
            var hasModelVersion = int.TryParse(ContextValue(context, "data_model_version"), out var modelVersion)
                                  && modelVersion > 0;
            if (string.IsNullOrWhiteSpace(modelId) || !hasModelVersion)
            {
                var processSpecificationId = ContextValue(context, "process_specification_id")?.Trim();
                var hasProcessSpecification = int.TryParse(ContextValue(context, "process_specification_version"), out var processSpecificationVersion)
                                       && processSpecificationVersion > 0;
                ProcessSpecification? processSpecification = null;
                if (!string.IsNullOrWhiteSpace(processSpecificationId) && hasProcessSpecification)
                {
                    var processSpecificationKey = (processSpecificationId.ToLowerInvariant(), processSpecificationVersion);
                    if (!processSpecificationCache.TryGetValue(processSpecificationKey, out processSpecification))
                    {
                        processSpecification = await store.GetProcessSpecificationAsync(processSpecificationKey.Item1, processSpecificationKey.processSpecificationVersion, ct).ConfigureAwait(false);
                        processSpecificationCache[processSpecificationKey] = processSpecification;
                    }
                }
                modelId = processSpecification?.DataModelId;
                modelVersion = processSpecification?.DataModelVersion ?? 0;
            }

            var plan = plans
                .Where(static item => item.Status == ConfigurationStatuses.Published)
                .Where(item => string.Equals(item.AnalysisScope, analysisScope, StringComparison.Ordinal))
                // An empty selector means "all contexts of this data model", never "all industries".
                // When the model cannot be inferred, only an explicit selector may select a plan.
                .Where(item => !string.IsNullOrWhiteSpace(modelId)
                    ? string.Equals(item.DataModelId, modelId, StringComparison.OrdinalIgnoreCase) &&
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

    public async Task<ProcessSpecification?> ResolveProcessSpecificationAsync(
        IReadOnlyDictionary<string, string> context,
        CancellationToken ct = default)
    {
        var values = await ResolveProcessSpecificationsAsync([context], ct).ConfigureAwait(false);
        return values[0];
    }

    public async Task<IReadOnlyList<ProcessSpecification?>> ResolveProcessSpecificationsAsync(
        IReadOnlyList<IReadOnlyDictionary<string, string>> contexts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        var cache = new Dictionary<(string Id, int Version), ProcessSpecification?>();
        var result = new List<ProcessSpecification?>(contexts.Count);
        foreach (var context in contexts)
        {
            ct.ThrowIfCancellationRequested();
        var processSpecificationId = ContextValue(context, "process_specification_id");
        var versionText = ContextValue(context, "process_specification_version");
        if (string.IsNullOrWhiteSpace(processSpecificationId) ||
            !int.TryParse(versionText, out var version) ||
            version < 1)
        {
                result.Add(null);
                continue;
        }

            var key = (processSpecificationId.Trim().ToLowerInvariant(), version);
            if (!cache.TryGetValue(key, out var processSpecification))
            {
                processSpecification = await store.GetProcessSpecificationAsync(key.Item1, key.version, ct)
                    .ConfigureAwait(false);
                cache[key] = processSpecification;
            }
            result.Add(processSpecification);
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
