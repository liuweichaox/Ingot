using System.Collections;
using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleComparisonService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore masterData) : ICycleComparisonService
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
        var productSeries = baselineEvents.SelectMany(static row => row.Event.Context)
            .Where(static pair => pair.Key == "product_series")
            .Select(static pair => pair.Value)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(productSeries))
            productSeries = "unknown";
        var completed = await QueryAllAsync(
            new PlatformEventQuery
            {
                EventType = "cycle.completed",
                Context = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["product_series"] = productSeries
                }
            },
            ct).ConfigureAwait(false);
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
        var allInspections = await inspections.QueryAllByOperationRunIdsAsync(allIds, ct).ConfigureAwait(false);
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allInspections.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var featureDefinitions = await masterData.ListFeatureDefinitionsAsync(ct).ConfigureAwait(false);
        var phaseDefinitions = await masterData.ListPhaseDefinitionsAsync(ct).ConfigureAwait(false);
        var phaseMappings = await masterData.ListPhaseMappingsAsync(ct).ConfigureAwait(false);
        var baselineContext = baselineEvents.Select(static row => row.Event.Context)
            .FirstOrDefault(static context => context.Count > 0) ?? baselineEvents[0].Event.Context;
        var baselineMachineId = baselineEvents[0].Event.Subject.Id;
        var comparisonSignals = featureDefinitions
            .Where(static definition => definition.Enabled && definition.UseInComparison)
            .Where(definition => MatchesFeatureScope(definition, baselineContext, baselineMachineId))
            .GroupBy(static definition => definition.Signal, StringComparer.Ordinal)
            .Select(static group => group.OrderBy(static definition => definition.Code, StringComparer.Ordinal).First())
            .ToArray();
        var inspectionsByCycle = allInspections.GroupBy(static record => record.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var rows = allIds.Select(id => BuildRow(
                id,
                cycleEvents[id],
                inspectionsByCycle.GetValueOrDefault(id, []),
                latestReviews,
                comparisonSignals,
                phaseDefinitions,
                phaseMappings))
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
            BaselineCycleId = correlationId,
            ProductSeries = productSeries,
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
        IReadOnlyList<FeatureDefinition> comparisonSignals,
        IReadOnlyList<PhaseDefinition> phaseDefinitions,
        IReadOnlyList<PhaseMapping> phaseMappings)
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
        var context = ordered.Select(static row => row.Event.Context).FirstOrDefault(static context => context.Count > 0)
                      ?? first.Event.Context;
        var recipeId = context.GetValueOrDefault("recipe_id");
        var recipeVersion = context.GetValueOrDefault("recipe_version");
        var applicableMappings = phaseMappings.Where(mapping =>
                string.Equals(mapping.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(mapping.RecipeVersion) ||
                 string.Equals(mapping.RecipeVersion, recipeVersion, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var phases = samples.Select(row => ResolvePhase(row.Event.Context, applicableMappings))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var requiredPhases = (applicableMappings.Length > 0
                ? applicableMappings.Where(static mapping => mapping.Required).Select(static mapping => mapping.PhaseCode)
                : phaseMappings.Count == 0
                    ? phaseDefinitions.Where(static phase => phase.Required).Select(static phase => phase.Code)
                    : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new CycleComparisonRow
        {
            CorrelationId = correlationId,
            MachineId = first.Event.Subject.Id,
            StartedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt,
            CompletedAt = completed?.Event.OccurredAt,
            DurationMs = completed is null ? null : (completed.Event.OccurredAt - (started?.Event.OccurredAt ?? first.Event.OccurredAt)).TotalMilliseconds,
            ProductSeries = context.GetValueOrDefault("product_series", "unknown"),
            ProductCode = context.GetValueOrDefault("product_code"),
            RecipeId = recipeId,
            RecipeVersion = recipeVersion,
            SampleCount = samples.Length,
            ExpectedSampleCount = expectedSampleCount,
            SampleCompleteness = expectedSampleCount == 0 ? 0 : samples.Length / (double)expectedSampleCount,
            PhaseCount = phases.Count,
            RequiredPhaseCount = requiredPhases.Length,
            PhaseComplete = requiredPhases.Length > 0 && requiredPhases.All(phases.Contains),
            InspectionOutcomes = inspectionRecords.Select(static record => record.Outcome).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            VisualReviewDecision = visualReview?.Decision,
            Signals = BuildSignalStatistics(samples, comparisonSignals)
        };
    }

    private static IReadOnlyList<CycleSignalStatistic> BuildSignalStatistics(
        IReadOnlyList<PlatformProductionEvent> samples,
        IReadOnlyList<FeatureDefinition> comparisonSignals)
    {
        var values = comparisonSignals.ToDictionary(static definition => definition.Signal, static _ => new List<double>(), StringComparer.Ordinal);
        foreach (var sample in samples)
        {
            if (!sample.Event.Data.TryGetValue("values", out var rawValues))
                continue;
            foreach (var signal in comparisonSignals.Select(static definition => definition.Signal))
            {
                if (TryReadNumber(rawValues, signal, out var number))
                    values[signal].Add(number);
            }
        }
        return comparisonSignals.Select(definition => new CycleSignalStatistic
        {
            Code = definition.Signal,
            Name = definition.Name,
            Unit = definition.Unit,
            SampleCount = values[definition.Signal].Count,
            Average = values[definition.Signal].Count == 0 ? null : values[definition.Signal].Average(),
            Minimum = values[definition.Signal].Count == 0 ? null : values[definition.Signal].Min(),
            Maximum = values[definition.Signal].Count == 0 ? null : values[definition.Signal].Max()
        }).ToArray();
    }

    private static string? ResolvePhase(
        IReadOnlyDictionary<string, string> context,
        IReadOnlyList<PhaseMapping> mappings)
    {
        var explicitPhase = context.GetValueOrDefault("process_phase");
        if (!string.IsNullOrWhiteSpace(explicitPhase))
            return explicitPhase;
        var recipeStep = context.GetValueOrDefault("recipe_step");
        if (string.IsNullOrWhiteSpace(recipeStep))
            return null;
        return mappings
                   .Where(mapping => string.Equals(mapping.RecipeStep, recipeStep, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(static mapping => !string.IsNullOrWhiteSpace(mapping.RecipeVersion))
                   .Select(static mapping => mapping.PhaseCode)
                   .FirstOrDefault()
               ?? recipeStep;
    }

    private static bool MatchesFeatureScope(
        FeatureDefinition definition,
        IReadOnlyDictionary<string, string> context,
        string machineId)
        => MatchesSelector(definition.ProductSeries, context.GetValueOrDefault("product_series")) &&
           MatchesSelector(definition.ProductCode, context.GetValueOrDefault("product_code")) &&
           MatchesSelector(definition.RecipeId, context.GetValueOrDefault("recipe_id")) &&
           MatchesSelector(definition.MachineId, machineId);

    private static bool MatchesSelector(string? selector, string? value)
        => string.IsNullOrWhiteSpace(selector) ||
           string.Equals(selector, value, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadNumber(object? container, string key, out double value)
    {
        value = 0;
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out var property) && property.TryGetDouble(out value))
        {
            return true;
        }
        if (container is IReadOnlyDictionary<string, object?> readOnly &&
            readOnly.TryGetValue(key, out var raw) && TryConvert(raw, out value))
        {
            return true;
        }
        if (container is IDictionary dictionary && dictionary.Contains(key) && TryConvert(dictionary[key], out value))
            return true;
        return false;
    }

    private static bool TryConvert(object? raw, out double value)
    {
        if (raw is JsonElement element && element.TryGetDouble(out value))
            return true;
        return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
