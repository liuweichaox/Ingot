using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleRecordService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore masterData,
    ProcessAnalysisResolver analysisResolver,
    WholeCycleAnalysisEngine? wholeCycleAnalysis = null,
    CycleAnalysisMaterializer? materializer = null) : ICycleRecordService
{
    private readonly WholeCycleAnalysisEngine _wholeCycleAnalysis = wholeCycleAnalysis ?? new();
    private readonly CycleAnalysisMaterializer? _materializer = materializer;

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
        int offset = 0,
        string? search = null,
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
                SearchText = Normalize(search),
                Context = context
            };
            lifecycle.AddRange(await QueryAllAsync(baseQuery with { EventType = "cycle.started" }, ct).ConfigureAwait(false));
            lifecycle.AddRange(await QueryAllAsync(baseQuery with { EventType = "cycle.completed" }, ct).ConfigureAwait(false));
        }

        var allCandidates = lifecycle
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
            .ToArray();
        var candidates = allCandidates
            .Skip(offset)
            .Take(limit)
            .ToArray();

        var ids = candidates.Select(static item => item.Id).ToArray();
        var selectedEvents = await events.QueryByCorrelationIdsAsync(ids, ct).ConfigureAwait(false);
        var cycleEvents = selectedEvents
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.CorrelationId))
            .GroupBy(static row => row.Event.CorrelationId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<PlatformProductionEvent>)group.ToArray(),
                StringComparer.Ordinal);
        var records = InspectionRecordSet.Effective(
            await inspections.QueryAllByOperationRunIdsAsync(ids, ct).ConfigureAwait(false));
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            records.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var plans = await masterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var analysisRows = await analysisResolver.ResolveManyAsync(
            ids.Select(id => ResolveContext(cycleEvents.GetValueOrDefault(id, []))).ToArray(),
            "production-cycle",
            ct).ConfigureAwait(false);
        var analyses = ids
            .Select((id, index) => (id, Analysis: analysisRows[index]))
            .ToDictionary(static pair => pair.id, static pair => pair.Analysis, StringComparer.Ordinal);
        var recordsByCycle = records.GroupBy(static record => record.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var materializedByCycle = new Dictionary<string, MaterializedCycleAnalysis>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            materializedByCycle[id] = await AnalyzeAsync(
                id,
                cycleEvents.GetValueOrDefault(id, []),
                analyses[id],
                ct).ConfigureAwait(false);
        }

        var rows = candidates.Select(candidate => BuildSummary(
                candidate.Id,
                cycleEvents.GetValueOrDefault(candidate.Id, []),
                recordsByCycle.GetValueOrDefault(candidate.Id, []),
                latestReviews,
                plans,
                analyses[candidate.Id],
                materializedByCycle[candidate.Id]))
            .ToArray();
        return new CycleRecordQueryResult
        {
            Data = rows,
            Total = allCandidates.Length,
            Overview = new CycleRecordOverview
            {
                CycleCount = allCandidates.Length,
                CompletedCount = allCandidates.Count(static row => row.Completed),
                ActiveCount = allCandidates.Count(static row => !row.Completed),
                SampleCompleteCount = rows.Count(static row =>
                    row.ProcessDataQuality.Status != ProcessDataStatuses.Unavailable),
                PhaseCompleteCount = rows.Count(static row => row.PhaseComplete == true),
                QualityCompleteCount = rows.Count(static row => row.QualityStatus == "COMPLETE"),
                IssueCycleCount = rows.Count(static row => row.DataIssues.Count > 0)
            }
        };
    }

    private CycleRecordSummary BuildSummary(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<InspectionRecord> inspectionRecords,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews,
        IReadOnlyList<InspectionPlan> plans,
        ResolvedProcessAnalysis? analysis,
        MaterializedCycleAnalysis materialized)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var started = ordered.FirstOrDefault(static row => row.Event.EventType == "cycle.started");
        var completed = ordered.LastOrDefault(static row => row.Event.EventType == "cycle.completed");
        var samples = ordered.Where(static row => row.Event.EventType == "process.sample").ToArray();
        var startedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt;
        var context = ResolveContext(ordered);
        var processAnalysis = materialized.Analysis;
        var requiredCodes = (analysis?.DataModel.Stages
                .Where(static stage => stage.Required)
                .Select(static stage => stage.Code) ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var phaseRows = processAnalysis.Phases;
        var observedCodes = phaseRows.Where(static phase => phase.Code != "unknown")
            .Select(static phase => phase.Code)
            .ToHashSet(StringComparer.Ordinal);

        var plan = InspectionPlanMatcher.Resolve(plans, context, first.Event.Subject.Id, startedAt);
        var requiredItems = plan?.Items.Where(static item => item.Required).ToArray() ?? [];
        var completedItems = requiredItems.Count(item => inspectionRecords.Any(record =>
            record.DefinitionCode == item.DefinitionCode && record.DefinitionVersion == item.DefinitionVersion));
        var pendingReviews = requiredItems.Where(static item => item.RequiresReview).Count(item =>
        {
            var latestRecord = inspectionRecords
                .Where(record => record.DefinitionCode == item.DefinitionCode &&
                                 record.DefinitionVersion == item.DefinitionVersion)
                .OrderByDescending(static record => record.MeasuredAt)
                .FirstOrDefault();
            return latestRecord is null ||
                   !latestReviews.TryGetValue(latestRecord.RecordId, out var review) ||
                   review.Decision != InspectionReviewDecisions.Confirmed;
        });
        var qualityStatus = ResolveQualityStatus(plan, requiredItems, completedItems, pendingReviews, inspectionRecords);
        var issues = BuildIssues(
            completed is not null,
            processAnalysis.Quality,
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
            ToolingInstallationId = context.GetValueOrDefault("tooling_installation_id"),
            ToolingId = context.GetValueOrDefault("tooling_id") ?? context.GetValueOrDefault("mold_id"),
            MoldId = context.GetValueOrDefault("mold_id"),
            AssemblyRevisionId = context.GetValueOrDefault("assembly_revision_id"),
            AssemblyRevision = context.GetValueOrDefault("assembly_revision"),
            ExternalOrderRef = context.GetValueOrDefault("external_order_ref"),
            ExternalBatchRef = context.GetValueOrDefault("external_batch_ref"),
            MaterialLotRef = context.GetValueOrDefault("material_lot_ref"),
            SampleCount = samples.Length,
            ExpectedSampleCount = 0,
            SampleCompleteness = processAnalysis.Quality.Status switch
            {
                ProcessDataStatuses.Available => 1d,
                ProcessDataStatuses.Degraded => 0.5d,
                _ => 0d
            },
            ProcessDataQuality = processAnalysis.Quality,
            PhaseCount = phaseRows.Count(static phase => phase.Code != "unknown"),
            RequiredPhaseCount = requiredCodes.Length,
            PhaseComplete = requiredCodes.Length == 0 ? null : requiredCodes.All(observedCodes.Contains),
            QualityStatus = qualityStatus,
            InspectionPlanId = plan?.PlanId,
            InspectionPlanVersion = plan?.Version,
            InspectionPlanName = plan?.Name,
            AnalysisPlanId = analysis?.Plan.PlanId,
            AnalysisPlanVersion = analysis?.Plan.Version,
            DataModelId = analysis?.DataModel.ModelId,
            DataModelVersion = analysis?.DataModel.Version,
            AnalysisMaterialization = materialized.Materialization,
            InspectionCount = inspectionRecords.Count,
            RequiredInspectionCount = requiredItems.Length,
            CompletedInspectionCount = completedItems,
            PendingReviewCount = pendingReviews,
            Phases = phaseRows,
            DataIssues = issues
        };
    }

    private async Task<MaterializedCycleAnalysis> AnalyzeAsync(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var startedAt = ordered.FirstOrDefault(static row => row.Event.EventType == "cycle.started")?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(static row => row.Event.EventType == "cycle.completed")?.Event.OccurredAt;
        if (_materializer is not null)
        {
            return await _materializer.GetOrComputeAsync(
                correlationId,
                ordered,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan,
                ct).ConfigureAwait(false);
        }

        return new MaterializedCycleAnalysis(
            _wholeCycleAnalysis.Analyze(
                ordered,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan),
            new CycleAnalysisMaterialization
            {
                Status = "query-time",
                AlgorithmVersion = WholeCycleAnalysisEngine.AlgorithmVersion,
                SourceMaxIngestId = ordered.Length == 0 ? 0 : ordered.Max(static row => row.IngestId),
                SourceEventCount = ordered.Length
            });
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
        ProcessDataQualitySummary processData,
        IReadOnlyDictionary<string, string> context)
    {
        var issues = new List<CycleDataIssue>();
        if (!completed)
            issues.Add(Issue("cycle.active", "info", "周期尚未结束。"));
        issues.AddRange(processData.Issues.Select(message => Issue(
            "process_data." + processData.Status,
            processData.Status == ProcessDataStatuses.Unavailable ? "error" : "warning",
            message)));
        foreach (var field in new[] { "product_series", "recipe_id", "workpiece_id" })
        {
            if (!context.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                issues.Add(Issue($"context.{field}.missing", "warning", $"生产信息缺少 {field}。"));
        }
        if (context.GetValueOrDefault("context_capture_status") == "configuration_missing")
        {
            issues.Add(Issue(
                "context.production_configuration_missing",
                "error",
                "周期开始时未找到唯一有效的生产准备和工装装卸记录。"));
        }
        return issues;
    }

    private static CycleDataIssue Issue(string code, string severity, string message)
        => new() { Code = code, Severity = severity, Message = message };

    private static IReadOnlyDictionary<string, string> ResolveContext(
        IReadOnlyList<PlatformProductionEvent> rows)
        => rows.Select(static row => row.Event.Context).FirstOrDefault(static value => value.Count > 0)
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

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
