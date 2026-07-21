using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleRecordService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore masterData) : ICycleRecordService
{
    public async Task<CycleRecordQueryResult> QueryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? productSeries,
        string? productCode,
        string? recipeId,
        string? machineId,
        string? workpieceId,
        string? correlationId,
        string? status,
        int limit,
        CancellationToken ct = default)
    {
        var context = BuildContext(productSeries, productCode, recipeId, workpieceId);
        var lifecycle = new List<PlatformProductionEvent>();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            lifecycle.AddRange(await QueryAllAsync(
                new PlatformEventQuery { CorrelationId = correlationId.Trim() }, ct).ConfigureAwait(false));
        }
        else
        {
            var baseQuery = new PlatformEventQuery
            {
                SubjectId = Normalize(machineId),
                From = from,
                To = to,
                Context = context
            };
            lifecycle.AddRange(await QueryAllAsync(baseQuery with { EventType = "cycle.started" }, ct).ConfigureAwait(false));
            lifecycle.AddRange(await QueryAllAsync(baseQuery with { EventType = "cycle.completed" }, ct).ConfigureAwait(false));
        }

        var candidates = lifecycle
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.CorrelationId))
            .GroupBy(static row => row.Event.CorrelationId!, StringComparer.Ordinal)
            .Select(group => new
            {
                Id = group.Key,
                StartedAt = group.Where(static row => row.Event.EventType == "cycle.started")
                    .Select(static row => row.Event.OccurredAt)
                    .DefaultIfEmpty(group.Min(static row => row.Event.OccurredAt))
                    .Min(),
                Completed = group.Any(static row => row.Event.EventType == "cycle.completed")
            })
            .Where(item => status switch
            {
                "completed" => item.Completed,
                "active" => !item.Completed,
                _ => true
            })
            .OrderByDescending(static item => item.StartedAt)
            .Take(limit)
            .ToArray();

        var cycleEvents = new Dictionary<string, IReadOnlyList<PlatformProductionEvent>>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            cycleEvents[candidate.Id] = await QueryAllAsync(
                new PlatformEventQuery { CorrelationId = candidate.Id }, ct).ConfigureAwait(false);
        }

        var ids = candidates.Select(static item => item.Id).ToArray();
        var records = await inspections.QueryAllByOperationRunIdsAsync(ids, ct).ConfigureAwait(false);
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            records.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var plans = await masterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var phaseDefinitions = await masterData.ListPhaseDefinitionsAsync(ct).ConfigureAwait(false);
        var phaseMappings = await masterData.ListPhaseMappingsAsync(ct).ConfigureAwait(false);
        var recordsByCycle = records.GroupBy(static record => record.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        var rows = candidates.Select(candidate => BuildSummary(
                candidate.Id,
                cycleEvents[candidate.Id],
                recordsByCycle.GetValueOrDefault(candidate.Id, []),
                latestReviews,
                plans,
                phaseDefinitions,
                phaseMappings))
            .ToArray();
        return new CycleRecordQueryResult
        {
            Data = rows,
            Overview = new CycleRecordOverview
            {
                CycleCount = rows.Length,
                CompletedCount = rows.Count(static row => row.Status == "completed"),
                ActiveCount = rows.Count(static row => row.Status == "active"),
                SampleCompleteCount = rows.Count(static row => row.SampleCompleteness >= 1d),
                PhaseCompleteCount = rows.Count(static row => row.PhaseComplete == true),
                QualityCompleteCount = rows.Count(static row => row.QualityStatus == "COMPLETE"),
                IssueCycleCount = rows.Count(static row => row.DataIssues.Count > 0)
            }
        };
    }

    private static CycleRecordSummary BuildSummary(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<InspectionRecord> inspectionRecords,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews,
        IReadOnlyList<InspectionPlan> plans,
        IReadOnlyList<PhaseDefinition> phaseDefinitions,
        IReadOnlyList<PhaseMapping> phaseMappings)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var started = ordered.FirstOrDefault(static row => row.Event.EventType == "cycle.started");
        var completed = ordered.LastOrDefault(static row => row.Event.EventType == "cycle.completed");
        var samples = ordered.Where(static row => row.Event.EventType == "process.sample").ToArray();
        var startedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt;
        var context = ordered.Select(static row => row.Event.Context).FirstOrDefault(static value => value.Count > 0)
                      ?? first.Event.Context;
        var expectedSampleCount = started is null ? 0 : ReadInt(started.Event.Data, "expectedSampleCount");
        var mappings = ApplicableMappings(phaseMappings, context);
        var resolvedSamples = samples.Select(sample => new
        {
            Row = sample,
            Phase = ResolvePhase(sample.Event.Context, mappings)
        }).ToArray();
        var requiredCodes = (mappings.Count > 0
                ? mappings.Where(static mapping => mapping.Required).Select(static mapping => mapping.PhaseCode)
                : phaseMappings.Count == 0
                    ? phaseDefinitions.Where(static phase => phase.Required).Select(static phase => phase.Code)
                    : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var phaseNames = phaseDefinitions.ToDictionary(static phase => phase.Code, static phase => phase.Name, StringComparer.Ordinal);
        var observedCodes = resolvedSamples.Where(static item => !string.IsNullOrWhiteSpace(item.Phase))
            .Select(static item => item.Phase!)
            .ToHashSet(StringComparer.Ordinal);
        var phaseRows = resolvedSamples.Where(static item => !string.IsNullOrWhiteSpace(item.Phase))
            .GroupBy(static item => item.Phase!, StringComparer.Ordinal)
            .Select(group => new CyclePhaseSummary
            {
                Code = group.Key,
                Name = phaseNames.GetValueOrDefault(group.Key, group.Key),
                Required = requiredCodes.Contains(group.Key, StringComparer.Ordinal),
                SampleCount = group.Count(),
                StartedAt = group.Min(static item => item.Row.Event.OccurredAt),
                EndedAt = group.Max(static item => item.Row.Event.OccurredAt)
            })
            .OrderBy(row => phaseDefinitions.FirstOrDefault(phase => phase.Code == row.Code)?.SortOrder ?? int.MaxValue)
            .ToArray();

        var plan = InspectionPlanMatcher.Resolve(plans, context, first.Event.Subject.Id, startedAt);
        var requiredItems = plan?.Items.Where(static item => item.Required).ToArray() ?? [];
        var completedItems = requiredItems.Count(item => inspectionRecords.Any(record =>
            record.DefinitionCode == item.DefinitionCode && record.DefinitionVersion == item.DefinitionVersion));
        var pendingReviews = requiredItems.Where(static item => item.RequiresReview).Count(item =>
        {
            var matching = inspectionRecords.Where(record => record.DefinitionCode == item.DefinitionCode &&
                                                             record.DefinitionVersion == item.DefinitionVersion).ToArray();
            return matching.Length == 0 || matching.All(record => !latestReviews.ContainsKey(record.RecordId));
        });
        var qualityStatus = ResolveQualityStatus(plan, requiredItems, completedItems, pendingReviews, inspectionRecords);
        var issues = BuildIssues(
            completed is not null,
            samples.Length,
            expectedSampleCount,
            requiredCodes,
            observedCodes,
            context);
        return new CycleRecordSummary
        {
            CorrelationId = correlationId,
            MachineId = first.Event.Subject.Id,
            Status = completed is null ? "active" : "completed",
            StartedAt = startedAt,
            CompletedAt = completed?.Event.OccurredAt,
            DurationMs = completed is null ? null : (completed.Event.OccurredAt - startedAt).TotalMilliseconds,
            WorkpieceId = context.GetValueOrDefault("workpiece_id"),
            ProductSeries = context.GetValueOrDefault("product_series"),
            ProductCode = context.GetValueOrDefault("product_code"),
            RecipeId = context.GetValueOrDefault("recipe_id"),
            RecipeVersion = context.GetValueOrDefault("recipe_version"),
            SampleCount = samples.Length,
            ExpectedSampleCount = expectedSampleCount,
            SampleCompleteness = expectedSampleCount > 0 ? samples.Length / (double)expectedSampleCount : null,
            PhaseCount = observedCodes.Count,
            RequiredPhaseCount = requiredCodes.Length,
            PhaseComplete = requiredCodes.Length == 0 ? null : requiredCodes.All(observedCodes.Contains),
            QualityStatus = qualityStatus,
            InspectionPlanId = plan?.PlanId,
            InspectionPlanVersion = plan?.Version,
            InspectionPlanName = plan?.Name,
            InspectionCount = inspectionRecords.Count,
            RequiredInspectionCount = requiredItems.Length,
            CompletedInspectionCount = completedItems,
            PendingReviewCount = pendingReviews,
            Phases = phaseRows,
            DataIssues = issues
        };
    }

    private static string ResolveQualityStatus(
        InspectionPlan? plan,
        IReadOnlyList<InspectionPlanItem> requiredItems,
        int completedItems,
        int pendingReviews,
        IReadOnlyList<InspectionRecord> records)
    {
        if (plan is null)
            return "NOT_APPLICABLE";
        if (records.Any(static record => record.Outcome == "FAIL"))
            return "FAILED";
        if (records.Any(static record => record.Outcome == "INCONCLUSIVE"))
            return "INCONCLUSIVE";
        if (completedItems < requiredItems.Count)
            return completedItems == 0 ? "PENDING" : "IN_PROGRESS";
        return pendingReviews > 0 ? "REVIEW_PENDING" : "COMPLETE";
    }

    private static IReadOnlyList<CycleDataIssue> BuildIssues(
        bool completed,
        int sampleCount,
        int expectedSampleCount,
        IReadOnlyList<string> requiredPhases,
        IReadOnlySet<string> observedPhases,
        IReadOnlyDictionary<string, string> context)
    {
        var issues = new List<CycleDataIssue>();
        if (!completed)
            issues.Add(Issue("cycle.active", "info", "周期尚未结束。"));
        if (expectedSampleCount <= 0)
            issues.Add(Issue("samples.expectation_missing", "warning", "未记录期望采样数，无法判断样本完整度。"));
        else if (sampleCount < expectedSampleCount)
            issues.Add(Issue("samples.incomplete", "error", $"样本缺少 {expectedSampleCount - sampleCount} 组。"));
        if (requiredPhases.Count == 0)
            issues.Add(Issue("phases.not_configured", "warning", "未配置必需阶段，无法判断阶段完整度。"));
        else
        {
            var missing = requiredPhases.Where(code => !observedPhases.Contains(code)).ToArray();
            if (missing.Length > 0)
                issues.Add(Issue("phases.incomplete", "error", $"缺少阶段：{string.Join("、", missing)}。"));
        }
        foreach (var field in new[] { "product_series", "recipe_id", "workpiece_id" })
        {
            if (!context.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                issues.Add(Issue($"context.{field}.missing", "warning", $"生产信息缺少 {field}。"));
        }
        return issues;
    }

    private static CycleDataIssue Issue(string code, string severity, string message)
        => new() { Code = code, Severity = severity, Message = message };

    private static IReadOnlyList<PhaseMapping> ApplicableMappings(
        IReadOnlyList<PhaseMapping> mappings,
        IReadOnlyDictionary<string, string> context)
    {
        var recipeId = context.GetValueOrDefault("recipe_id");
        var recipeVersion = context.GetValueOrDefault("recipe_version");
        return mappings.Where(mapping =>
                string.Equals(mapping.RecipeId, recipeId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(mapping.RecipeVersion) ||
                 string.Equals(mapping.RecipeVersion, recipeVersion, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static string? ResolvePhase(
        IReadOnlyDictionary<string, string> context,
        IReadOnlyList<PhaseMapping> mappings)
    {
        var explicitPhase = context.GetValueOrDefault("process_phase");
        if (!string.IsNullOrWhiteSpace(explicitPhase))
            return explicitPhase;
        var step = context.GetValueOrDefault("recipe_step");
        return string.IsNullOrWhiteSpace(step)
            ? null
            : mappings.Where(mapping => string.Equals(mapping.RecipeStep, step, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static mapping => !string.IsNullOrWhiteSpace(mapping.RecipeVersion))
                .Select(static mapping => mapping.PhaseCode)
                .FirstOrDefault() ?? step;
    }

    private static IReadOnlyDictionary<string, string> BuildContext(
        string? productSeries,
        string? productCode,
        string? recipeId,
        string? workpieceId)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(result, "product_series", productSeries);
        Add(result, "product_code", productCode);
        Add(result, "recipe_id", recipeId);
        Add(result, "workpiece_id", workpieceId);
        return result;
    }

    private static void Add(IDictionary<string, string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            target[key] = value.Trim();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int ReadInt(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var raw))
            return 0;
        if (raw is JsonElement element && element.TryGetInt32(out var value))
            return value;
        return int.TryParse(Convert.ToString(raw), out value) ? value : 0;
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
                throw new InvalidOperationException("生产周期查询的摄入游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }
}
