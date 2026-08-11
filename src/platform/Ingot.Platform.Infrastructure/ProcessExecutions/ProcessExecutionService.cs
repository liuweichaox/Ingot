using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore masterData,
    ProcessAnalysisResolver analysisResolver,
    ProcessExecutionAnalysisEngine? wholeProcessExecutionAnalysis = null,
    ProcessExecutionAnalysisMaterializer? materializer = null) : IProcessExecutionService
{
    private readonly ProcessExecutionAnalysisEngine _wholeProcessExecutionAnalysis = wholeProcessExecutionAnalysis ?? new();
    private readonly ProcessExecutionAnalysisMaterializer? _materializer = materializer;

    public async Task<ProcessExecutionQueryResult> QueryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? productFamilyCode,
        string? productCode,
        string? processSpecificationId,
        string? equipmentId,
        string? outputItemId,
        string? executionId,
        string? status,
        int limit,
        int offset = 0,
        string? search = null,
        CancellationToken ct = default,
        string? edgeId = null,
        string? externalBatchRef = null)
    {
        var context = BuildContext(productFamilyCode, productCode, processSpecificationId, outputItemId, externalBatchRef);
        var lifecycle = new List<PlatformProductionEvent>();
        if (!string.IsNullOrWhiteSpace(executionId))
        {
            lifecycle.AddRange(await QueryAllAsync(
                new PlatformEventQuery { ExecutionId = executionId.Trim() }, ct).ConfigureAwait(false));
        }
        else
        {
            var baseQuery = new PlatformEventQuery
            {
                EdgeId = Normalize(edgeId),
                SubjectId = Normalize(equipmentId),
                From = from,
                To = to,
                SearchText = Normalize(search),
                Context = context
            };
            lifecycle.AddRange(await QueryAllAsync(baseQuery with { EventType = "process.execution.started" }, ct).ConfigureAwait(false));
            lifecycle.AddRange(await QueryAllAsync(baseQuery with { EventType = "process.execution.completed" }, ct).ConfigureAwait(false));
        }

        var allCandidates = lifecycle
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.ExecutionId))
            .GroupBy(static row => row.Event.ExecutionId!, StringComparer.Ordinal)
            .Select(group => new
            {
                Id = group.Key,
                StartedAt = group.Where(static row => row.Event.EventType == "process.execution.started")
                    .Select(static row => row.Event.OccurredAt)
                    .DefaultIfEmpty(group.Min(static row => row.Event.OccurredAt))
                    .Min(),
                HasStarted = group.Any(static row => row.Event.EventType == "process.execution.started"),
                HasCompleted = group.Any(static row => row.Event.EventType == "process.execution.completed")
            })
            .Where(item => status switch
            {
                "completed" => item.HasStarted && item.HasCompleted,
                "active" => item.HasStarted && !item.HasCompleted,
                "incomplete" => !item.HasStarted,
                _ => true
            })
            .OrderByDescending(static item => item.StartedAt)
            .ToArray();
        var candidates = allCandidates
            .Skip(offset)
            .Take(limit)
            .ToArray();

        var ids = candidates.Select(static item => item.Id).ToArray();
        var selectedEvents = await events.QueryByExecutionIdsAsync(ids, ct).ConfigureAwait(false);
        var executionEvents = selectedEvents
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.ExecutionId))
            .GroupBy(static row => row.Event.ExecutionId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<PlatformProductionEvent>)group.ToArray(),
                StringComparer.Ordinal);
        var records = InspectionRecordSet.Effective(
            await inspections.QueryAllByExecutionIdsAsync(ids, ct).ConfigureAwait(false));
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            records.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var plans = await masterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var analysisRows = await analysisResolver.ResolveManyAsync(
            ids.Select(id => ResolveContext(executionEvents.GetValueOrDefault(id, []))).ToArray(),
            "production-execution",
            ct).ConfigureAwait(false);
        var analyses = ids
            .Select((id, index) => (id, Analysis: analysisRows[index]))
            .ToDictionary(static pair => pair.id, static pair => pair.Analysis, StringComparer.Ordinal);
        var recordsByProcessExecution = records.GroupBy(static record => record.ExecutionId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var materializedByProcessExecution = new Dictionary<string, MaterializedProcessExecutionAnalysis>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            materializedByProcessExecution[id] = await AnalyzeAsync(
                id,
                executionEvents.GetValueOrDefault(id, []),
                analyses[id],
                ct).ConfigureAwait(false);
        }

        var rows = candidates.Select(candidate => BuildSummary(
                candidate.Id,
                executionEvents.GetValueOrDefault(candidate.Id, []),
                recordsByProcessExecution.GetValueOrDefault(candidate.Id, []),
                latestReviews,
                plans,
                analyses[candidate.Id],
                materializedByProcessExecution[candidate.Id]))
            .ToArray();
        return new ProcessExecutionQueryResult
        {
            Data = rows,
            Total = allCandidates.Length,
            Overview = new ProcessExecutionOverview
            {
                ExecutionCount = allCandidates.Length,
                CompletedCount = allCandidates.Count(static row => row.HasStarted && row.HasCompleted),
                ActiveCount = allCandidates.Count(static row => row.HasStarted && !row.HasCompleted),
                IncompleteCount = allCandidates.Count(static row => !row.HasStarted),
                SampleCompleteCount = rows.Count(static row =>
                    row.ProcessDataQuality.Status != ProcessDataStatuses.Unavailable),
                QualityCompleteCount = rows.Count(static row => row.QualityStatus == "COMPLETE"),
                IssueExecutionCount = rows.Count(static row => row.DataIssues.Count > 0)
            }
        };
    }

    private ProcessExecutionSummary BuildSummary(
        string executionId,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<InspectionRecord> inspectionRecords,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews,
        IReadOnlyList<InspectionPlan> plans,
        ResolvedProcessAnalysis? analysis,
        MaterializedProcessExecutionAnalysis materialized)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var started = ordered.FirstOrDefault(static row => row.Event.EventType == "process.execution.started");
        var completed = ordered.LastOrDefault(static row => row.Event.EventType == "process.execution.completed");
        var samples = ordered.Where(static row => row.Event.EventType == "process.sample").ToArray();
        var startedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt;
        var context = ResolveContext(ordered);
        var processAnalysis = materialized.Analysis;
        var phaseRows = processAnalysis.Phases;
        var lifecycleComplete = started is not null && completed is not null;

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
            started is not null,
            completed is not null,
            processAnalysis.Quality,
            context);
        return new ProcessExecutionSummary
        {
            ExecutionId = executionId,
            EquipmentId = first.Event.Subject.Id,
            EdgeIds = ordered.Select(static row => row.EdgeId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Status = lifecycleComplete ? "completed" : started is not null ? "active" : "incomplete",
            HasStarted = started is not null,
            HasCompleted = completed is not null,
            LifecycleComplete = lifecycleComplete,
            StartedAt = startedAt,
            CompletedAt = completed?.Event.OccurredAt,
            DurationMs = lifecycleComplete
                ? (completed!.Event.OccurredAt - started!.Event.OccurredAt).TotalMilliseconds
                : null,
            OutputItemId = context.GetValueOrDefault("output_item_id"),
            ProductFamilyCode = context.GetValueOrDefault("product_family_code"),
            ProductCode = context.GetValueOrDefault("product_code"),
            ProcessSpecificationId = context.GetValueOrDefault("process_specification_id"),
            ProcessSpecificationVersion = context.GetValueOrDefault("process_specification_version"),
            ToolingInstallationId = context.GetValueOrDefault("tooling_installation_id"),
            ToolingAssemblyId = context.GetValueOrDefault("tooling_assembly_id"),
            AssemblyRevisionId = context.GetValueOrDefault("assembly_revision_id"),
            AssemblyRevision = context.GetValueOrDefault("assembly_revision"),
            ExternalOrderRef = context.GetValueOrDefault("external_order_ref"),
            ExternalBatchRef = context.GetValueOrDefault("external_batch_ref"),
            MaterialLotRef = context.GetValueOrDefault("material_lot_ref"),
            SampleCount = samples.Length,
            ExpectedSampleCount = 0,
            ProcessDataQuality = processAnalysis.Quality,
            PhaseCount = phaseRows.Count(static phase => phase.Code != "unknown"),
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

    private async Task<MaterializedProcessExecutionAnalysis> AnalyzeAsync(
        string executionId,
        IReadOnlyList<PlatformProductionEvent> rows,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var startedAt = ordered.FirstOrDefault(static row => row.Event.EventType == "process.execution.started")?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(static row => row.Event.EventType == "process.execution.completed")?.Event.OccurredAt;
        if (_materializer is not null)
        {
            return await _materializer.GetOrComputeAsync(
                executionId,
                ordered,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan,
                ct).ConfigureAwait(false);
        }

        var source = ProcessExecutionAnalysisMaterializer.CreateSourceFingerprint(ordered);
        return new MaterializedProcessExecutionAnalysis(
            _wholeProcessExecutionAnalysis.Analyze(
                ordered,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan),
            new ProcessExecutionAnalysisMaterialization
            {
                Status = "query-time",
                AlgorithmVersion = ProcessExecutionAnalysisEngine.AlgorithmVersion,
                SourceMinIngestId = source.MinIngestId,
                SourceMaxIngestId = source.MaxIngestId,
                SourceEventCount = source.EventCount,
                SourceContentHash = source.ContentHash
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

    private static IReadOnlyList<ExecutionDataIssue> BuildIssues(
        bool hasStarted,
        bool hasCompleted,
        ProcessDataQualitySummary processData,
        IReadOnlyDictionary<string, string> context)
    {
        var issues = new List<ExecutionDataIssue>();
        if (!hasStarted)
            issues.Add(Issue("execution.start.missing", "error", "未找到生产开始事件。"));
        if (!hasCompleted)
            issues.Add(Issue(
                "execution.end.missing",
                hasStarted ? "info" : "error",
                hasStarted ? "生产已开始，尚未收到生产结束事件。" : "未找到生产结束事件。"));
        issues.AddRange(processData.Issues.Select(message => Issue(
            "process_data." + processData.Status,
            processData.Status == ProcessDataStatuses.Unavailable ? "error" : "warning",
            message)));
        foreach (var field in new[] { "product_family_code", "process_specification_id", "output_item_id" })
        {
            if (!context.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
                issues.Add(Issue($"context.{field}.missing", "warning", $"生产信息缺少 {field}。"));
        }
        if (context.GetValueOrDefault("context_capture_status") == "configuration_missing")
        {
            issues.Add(Issue(
                "context.production_configuration_missing",
                "error",
                "过程执行开始时未找到唯一有效的生产准备和工装装卸记录。"));
        }
        return issues;
    }

    private static ExecutionDataIssue Issue(string code, string severity, string message)
        => new() { Code = code, Severity = severity, Message = message };

    private static IReadOnlyDictionary<string, string> ResolveContext(
        IReadOnlyList<PlatformProductionEvent> rows)
        => rows.Select(static row => row.Event.Context).FirstOrDefault(static value => value.Count > 0)
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> BuildContext(
        string? productFamilyCode,
        string? productCode,
        string? processSpecificationId,
        string? outputItemId,
        string? externalBatchRef)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(result, "product_family_code", productFamilyCode);
        Add(result, "product_code", productCode);
        Add(result, "process_specification_id", processSpecificationId);
        Add(result, "output_item_id", outputItemId);
        Add(result, "external_batch_ref", externalBatchRef);
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
                throw new InvalidOperationException("生产过程执行查询的摄入游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }
}
