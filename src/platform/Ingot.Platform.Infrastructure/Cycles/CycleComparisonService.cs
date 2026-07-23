using System.Collections;
using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleComparisonService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    ProcessAnalysisResolver analysisResolver) : ICycleComparisonService
{
    public async Task<CycleComparisonResult?> CompareWithHistoryAsync(
        string correlationId,
        int limit,
        CancellationToken ct = default)
    {
        var baselineEvents = await QueryAllAsync(
            new PlatformEventQuery { CorrelationId = correlationId }, ct).ConfigureAwait(false);
        if (baselineEvents.Count == 0)
            return null;

        var baselineContext = ResolveContext(baselineEvents);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, "production-cycle", ct)
            .ConfigureAwait(false);
        EnsureComparisonKeysPresent(analysis?.Plan, baselineContext, "基准周期");
        var comparisonContext = BuildComparisonContext(analysis?.Plan, baselineContext);
        var completed = await QueryAllAsync(
            new PlatformEventQuery { EventType = "cycle.completed", Context = comparisonContext }, ct)
            .ConfigureAwait(false);
        var candidateIds = completed
            .Where(item => !string.IsNullOrWhiteSpace(item.Event.CorrelationId) &&
                           !string.Equals(item.Event.CorrelationId, correlationId, StringComparison.Ordinal))
            .GroupBy(static item => item.Event.CorrelationId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Take(limit)
            .Select(static item => item.Event.CorrelationId!)
            .ToArray();
        var cycleEvents = new Dictionary<string, IReadOnlyList<PlatformProductionEvent>>(StringComparer.Ordinal)
        {
            [correlationId] = baselineEvents
        };
        foreach (var candidateId in candidateIds)
        {
            cycleEvents[candidateId] = await QueryAllAsync(
                new PlatformEventQuery { CorrelationId = candidateId }, ct).ConfigureAwait(false);
        }
        var allIds = new[] { correlationId }.Concat(candidateIds).ToArray();
        return await BuildComparisonAsync(correlationId, allIds, cycleEvents, analysis, ct).ConfigureAwait(false);
    }

    public async Task<CycleComparisonResult?> CompareSelectedAsync(
        string baselineCycleId,
        IReadOnlyList<string> cycleIds,
        CancellationToken ct = default)
    {
        var allIds = new[] { baselineCycleId }
            .Concat(cycleIds.Where(id => !string.Equals(id, baselineCycleId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (allIds.Length < 2)
            throw new ArgumentException("请选择至少两个不同的生产周期。", nameof(cycleIds));

        var cycleEvents = new Dictionary<string, IReadOnlyList<PlatformProductionEvent>>(StringComparer.Ordinal);
        foreach (var cycleId in allIds)
        {
            var rows = await QueryAllAsync(
                new PlatformEventQuery { CorrelationId = cycleId }, ct).ConfigureAwait(false);
            if (rows.Count == 0)
                return null;
            cycleEvents[cycleId] = rows;
        }

        var baselineContext = ResolveContext(cycleEvents[baselineCycleId]);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, "production-cycle", ct)
            .ConfigureAwait(false);
        var comparisonKeys = ResolveComparisonKeys(analysis?.Plan);
        EnsureComparisonKeysPresent(analysis?.Plan, baselineContext, "基准周期");
        var incompatible = allIds.Skip(1).FirstOrDefault(id =>
        {
            var candidateContext = ResolveContext(cycleEvents[id]);
            EnsureComparisonKeysPresent(analysis?.Plan, candidateContext, $"周期 {id}");
            return !ContextsMatch(baselineContext, candidateContext, comparisonKeys);
        });
        if (incompatible is not null)
        {
            throw new ArgumentException(
                $"周期 {incompatible} 与基准周期的同类比较键不一致：{string.Join("、", comparisonKeys)}。",
                nameof(cycleIds));
        }

        return await BuildComparisonAsync(baselineCycleId, allIds, cycleEvents, analysis, ct)
            .ConfigureAwait(false);
    }

    private async Task<CycleComparisonResult> BuildComparisonAsync(
        string baselineCycleId,
        IReadOnlyList<string> allIds,
        IReadOnlyDictionary<string, IReadOnlyList<PlatformProductionEvent>> cycleEvents,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var allInspections = InspectionRecordSet.Effective(
            await inspections.QueryAllByOperationRunIdsAsync(allIds, ct).ConfigureAwait(false));
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allInspections.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var recipesByCycle = new Dictionary<string, RecipeVersion?>(StringComparer.Ordinal);
        foreach (var id in allIds)
        {
            recipesByCycle[id] = await analysisResolver.ResolveRecipeAsync(ResolveContext(cycleEvents[id]), ct)
                .ConfigureAwait(false);
        }
        var inspectionsByCycle = allInspections.GroupBy(static record => record.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var rows = allIds.Select(id => BuildRow(
                id,
                cycleEvents[id],
                inspectionsByCycle.GetValueOrDefault(id, []),
                latestReviews,
                analysis,
                recipesByCycle[id]))
            .ToArray();
        var acceptance = new CycleComparisonAcceptance
        {
            CycleCount = rows.Length,
            CompleteCycleCount = rows.Count(static row => row.SampleCompleteness >= 1d && row.CompletedAt.HasValue),
            PhaseCompleteCycleCount = rows.Count(static row => row.PhaseComplete),
            QualityLinkedCycleCount = rows.Count(static row => row.InspectionOutcomes.Count > 0),
            VisualReviewCompletedCycleCount = rows.Count(static row => !string.IsNullOrWhiteSpace(row.VisualReviewDecision))
        };
        return new CycleComparisonResult
        {
            BaselineCycleId = baselineCycleId,
            ProductSeries = rows[0].ProductSeries,
            AnalysisPlanId = analysis?.Plan.PlanId,
            AnalysisPlanVersion = analysis?.Plan.Version,
            DataModelId = analysis?.DataModel.ModelId,
            DataModelVersion = analysis?.DataModel.Version,
            AnalysisScope = analysis?.Plan.AnalysisScope ?? "production-cycle",
            AlignmentMode = analysis?.Plan.AlignmentMode,
            Baseline = rows[0],
            HistoricalCycles = rows.Skip(1).ToArray(),
            Acceptance = acceptance
        };
    }

    private static CycleComparisonRow BuildRow(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<InspectionRecord> inspectionRecords,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews,
        ResolvedProcessAnalysis? analysis,
        RecipeVersion? recipe)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var started = ordered.FirstOrDefault(static row => row.Event.EventType == "cycle.started");
        var completed = ordered.LastOrDefault(static row => row.Event.EventType == "cycle.completed");
        var samples = ordered.Where(static row => row.Event.EventType == "process.sample").ToArray();
        var expectedSampleCount = started is null ? 0 : ReadInt(started.Event.Data, "expectedSampleCount");
        if (expectedSampleCount <= 0)
            expectedSampleCount = samples.Length;
        var visualRecord = inspectionRecords.Where(static record => record.Attachments.Count > 0)
            .OrderByDescending(static record => record.MeasuredAt)
            .FirstOrDefault();
        var visualReview = visualRecord is null ? null : latestReviews.GetValueOrDefault(visualRecord.RecordId);
        var context = ResolveContext(ordered);
        var phases = samples.Select(row => analysis is null
                ? null
                : ProcessAnalysisResolver.ResolveStage(row.Event.Context, analysis.DataModel))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var requiredPhases = analysis?.DataModel.Stages
            .Where(static stage => stage.Required)
            .Select(static stage => stage.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        return new CycleComparisonRow
        {
            CorrelationId = correlationId,
            MachineId = first.Event.Subject.Id,
            StartedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt,
            CompletedAt = completed?.Event.OccurredAt,
            DurationMs = completed is null ? null :
                (completed.Event.OccurredAt - (started?.Event.OccurredAt ?? first.Event.OccurredAt)).TotalMilliseconds,
            ProductSeries = ProcessAnalysisResolver.ContextValue(context, "product_series") ?? "unknown",
            ProductCode = ProcessAnalysisResolver.ContextValue(context, "product_code"),
            RecipeId = ProcessAnalysisResolver.ContextValue(context, "recipe_id"),
            RecipeVersion = ProcessAnalysisResolver.ContextValue(context, "recipe_version"),
            ToolingInstallationId = ProcessAnalysisResolver.ContextValue(context, "tooling_installation_id"),
            ToolingId = ProcessAnalysisResolver.ContextValue(context, "tooling_id") ??
                        ProcessAnalysisResolver.ContextValue(context, "mold_id"),
            MoldId = ProcessAnalysisResolver.ContextValue(context, "mold_id"),
            AssemblyRevisionId = ProcessAnalysisResolver.ContextValue(context, "assembly_revision_id"),
            AssemblyRevision = ProcessAnalysisResolver.ContextValue(context, "assembly_revision"),
            SampleCount = samples.Length,
            ExpectedSampleCount = expectedSampleCount,
            SampleCompleteness = expectedSampleCount == 0 ? 0 : samples.Length / (double)expectedSampleCount,
            PhaseCount = phases.Count,
            RequiredPhaseCount = requiredPhases.Length,
            PhaseComplete = requiredPhases.Length > 0 && requiredPhases.All(phases.Contains),
            InspectionOutcomes = inspectionRecords.Select(static record => record.Outcome)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            VisualReviewDecision = visualReview?.Decision,
            Signals = BuildSignalStatistics(samples, analysis),
            RecipeParameters = BuildRecipeParameters(recipe, analysis?.DataModel)
        };
    }

    private static IReadOnlyList<CycleSignalStatistic> BuildSignalStatistics(
        IReadOnlyList<PlatformProductionEvent> samples,
        ResolvedProcessAnalysis? analysis)
    {
        if (analysis is null)
            return [];
        var items = analysis.DataModel.Acquisition.DataItems.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var comparisonSignals = analysis.Plan.Signals
            .Where(selection => items.ContainsKey(selection.DataItemCode))
            .Select(selection => items[selection.DataItemCode])
            .ToArray();
        var values = comparisonSignals.ToDictionary(static definition => definition.Code, static _ => new List<double>(), StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            if (!sample.Event.Data.TryGetValue("values", out var rawValues))
                continue;
            foreach (var signal in comparisonSignals.Select(static definition => definition.Code))
            {
                if (TryReadNumber(rawValues, signal, out var number))
                    values[signal].Add(number);
            }
        }
        return comparisonSignals.Select(definition => new CycleSignalStatistic
        {
            Code = definition.Code,
            Name = definition.SourceField,
            Unit = definition.Unit,
            SampleCount = values[definition.Code].Count,
            Average = values[definition.Code].Count == 0 ? null : values[definition.Code].Average(),
            Minimum = values[definition.Code].Count == 0 ? null : values[definition.Code].Min(),
            Maximum = values[definition.Code].Count == 0 ? null : values[definition.Code].Max()
        }).ToArray();
    }

    private static IReadOnlyList<CycleRecipeParameter> BuildRecipeParameters(
        RecipeVersion? recipe,
        ProcessDataModel? model)
    {
        if (recipe is null)
            return [];
        var definitions = model?.RecipeParameters.ToDictionary(static item => item.Code, StringComparer.Ordinal)
                          ?? new Dictionary<string, RecipeParameterDefinition>(StringComparer.Ordinal);
        return recipe.Values.Select(value =>
        {
            definitions.TryGetValue(value.Code, out var definition);
            return new CycleRecipeParameter
            {
                Code = value.Code,
                Name = definition?.SourceField,
                Unit = definition?.Unit,
                Value = value.Value
            };
        }).ToArray();
    }

    private static IReadOnlyDictionary<string, string> BuildComparisonContext(
        ProcessAnalysisPlan? plan,
        IReadOnlyDictionary<string, string> baselineContext)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in ResolveComparisonKeys(plan))
        {
            var value = ProcessAnalysisResolver.ContextValue(baselineContext, key);
            if (!string.IsNullOrWhiteSpace(value))
                result[ToEventContextKey(key)] = value;
        }
        return result;
    }

    private static IReadOnlyList<string> ResolveComparisonKeys(ProcessAnalysisPlan? plan)
        => plan?.ComparisonKeys.Count > 0 ? plan.ComparisonKeys : ["product_series"];

    private static void EnsureComparisonKeysPresent(
        ProcessAnalysisPlan? plan,
        IReadOnlyDictionary<string, string> context,
        string source)
    {
        var missing = ResolveComparisonKeys(plan)
            .Where(key => string.IsNullOrWhiteSpace(ProcessAnalysisResolver.ContextValue(context, key)))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"{source}缺少同类比较上下文：{string.Join("、", missing)}。");
    }

    private static bool ContextsMatch(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> candidate,
        IReadOnlyList<string> keys)
        => keys.All(key => string.Equals(
            ProcessAnalysisResolver.ContextValue(baseline, key),
            ProcessAnalysisResolver.ContextValue(candidate, key),
            StringComparison.OrdinalIgnoreCase));

    private static string ToEventContextKey(string key) => key.Replace('.', '_');

    private static IReadOnlyDictionary<string, string> ResolveContext(
        IReadOnlyList<PlatformProductionEvent> rows)
        => rows.Select(static row => row.Event.Context).FirstOrDefault(static context => context.Count > 0)
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static bool TryReadNumber(object? container, string key, out double value)
    {
        value = 0;
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var property) && property.TryGetDouble(out value))
            return true;
        if (container is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(key, out var raw) && TryConvert(raw, out value))
            return true;
        return container is IDictionary dictionary && dictionary.Contains(key) && TryConvert(dictionary[key], out value);
    }

    private static bool TryConvert(object? raw, out double value)
    {
        if (raw is JsonElement element && element.TryGetDouble(out value))
            return true;
        return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var raw))
            return 0;
        if (raw is JsonElement element && element.TryGetInt32(out var parsed))
            return parsed;
        return int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out parsed) ? parsed : 0;
    }

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        CancellationToken ct)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var page = await events.QueryAsync(query with { AfterIngestId = cursor, Limit = 500 }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("历史周期比较查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }
}
