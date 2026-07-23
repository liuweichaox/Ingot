using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionWorkflowService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore masterData) : IInspectionWorkflowService
{
    public async Task<InspectionTaskSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var tasks = await QueryTasksAsync("all", int.MaxValue, ct).ConfigureAwait(false);
        return new InspectionTaskSummary
        {
            Total = tasks.Count,
            Pending = tasks.Count(static task => task.Status == "pending"),
            InProgress = tasks.Count(static task => task.Status == "in_progress"),
            ReviewPending = tasks.Count(static task => task.Status == "review_pending"),
            Completed = tasks.Count(static task => task.Status == "completed")
        };
    }

    public async Task<InspectionTask?> GetTaskAsync(string operationRunId, CancellationToken ct = default)
    {
        var completed = (await QueryAllAsync(
                new PlatformEventQuery { CorrelationId = operationRunId, EventType = "cycle.completed" }, ct)
            .ConfigureAwait(false))
            .OrderByDescending(static item => item.Event.OccurredAt)
            .ThenByDescending(static item => item.IngestId)
            .FirstOrDefault();
        if (completed is null)
        {
            var scope = await inspections.GetScopeAsync(operationRunId, ct).ConfigureAwait(false);
            if (scope is null) return null;
            var scopePlan = await masterData.GetInspectionPlanAsync(
                scope.InspectionPlanId, scope.InspectionPlanVersion, ct).ConfigureAwait(false);
            if (scopePlan is null || scopePlan.Status != InspectionPlanStatuses.Published) return null;
            var scopeRecords = await inspections.QueryAllByOperationRunIdsAsync([operationRunId], ct).ConfigureAwait(false);
            var scopeReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
                scopeRecords.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
            return BuildTask(scope, scopePlan, scopeRecords, scopeReviews);
        }
        var plans = await masterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var plan = InspectionPlanMatcher.Resolve(plans, completed.Event.Context, completed.Event.Subject.Id, completed.Event.OccurredAt);
        if (plan is null) return null;
        var records = await inspections.QueryAllByOperationRunIdsAsync([operationRunId], ct).ConfigureAwait(false);
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            records.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        return BuildTask(completed, plan, records, latestReviews);
    }

    public async Task<IReadOnlyList<InspectionTask>> QueryTasksAsync(
        string? status,
        int limit,
        CancellationToken ct = default)
    {
        var completedEvents = await QueryAllAsync(
            new PlatformEventQuery { EventType = "cycle.completed" }, ct).ConfigureAwait(false);
        var completed = completedEvents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.CorrelationId))
            .GroupBy(static item => item.Event.CorrelationId!, StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static item => item.Event.OccurredAt)
                .ThenByDescending(static item => item.IngestId)
                .First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .ToArray();
        var scopes = await inspections.ListScopesAsync(ct).ConfigureAwait(false);
        var cycleIds = completed.Select(static item => item.Event.CorrelationId!).ToHashSet(StringComparer.Ordinal);
        scopes = scopes.Where(scope => !cycleIds.Contains(scope.ScopeId)).ToArray();
        var operationRunIds = completed.Select(static item => item.Event.CorrelationId!)
            .Concat(scopes.Select(static scope => scope.ScopeId)).ToArray();
        var records = await inspections.QueryAllByOperationRunIdsAsync(operationRunIds, ct).ConfigureAwait(false);
        var plans = await masterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            records.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var byCycle = records.GroupBy(static record => record.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        var cycleTasks = completed
            .Select(item => new
            {
                Completed = item,
                Plan = InspectionPlanMatcher.Resolve(
                    plans,
                    item.Event.Context,
                    item.Event.Subject.Id,
                    item.Event.OccurredAt)
            })
            .Where(static item => item.Plan is not null)
            .Select(item => BuildTask(
                item.Completed,
                item.Plan!,
                byCycle.GetValueOrDefault(item.Completed.Event.CorrelationId!, []),
                latestReviews))
            .ToArray();
        var planByKey = plans.ToDictionary(item => (item.PlanId, item.Version));
        var scopeTasks = scopes
            .Where(scope => planByKey.ContainsKey((scope.InspectionPlanId, scope.InspectionPlanVersion)))
            .Select(scope => BuildTask(
                scope,
                planByKey[(scope.InspectionPlanId, scope.InspectionPlanVersion)],
                byCycle.GetValueOrDefault(scope.ScopeId, []),
                latestReviews));
        var tasks = cycleTasks.Concat(scopeTasks)
            .OrderByDescending(static task => task.CompletedAt)
            .Where(task => string.IsNullOrWhiteSpace(normalizedStatus) || normalizedStatus == "all" ||
                           string.Equals(task.Status, normalizedStatus, StringComparison.Ordinal))
            .Take(limit).ToArray();
        return tasks;
    }

    public async Task<InspectionTaskPage> QueryTaskPageAsync(
        string? status,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var all = await QueryTasksAsync(status, int.MaxValue, ct).ConfigureAwait(false);
        return new InspectionTaskPage
        {
            Data = all.Skip(offset).Take(limit).ToArray(),
            Total = all.Count,
            Offset = offset,
            Limit = limit
        };
    }

    private InspectionTask BuildTask(
        PlatformProductionEvent completed,
        InspectionPlan plan,
        IReadOnlyList<InspectionRecord> records,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews)
    {
        records = InspectionRecordSet.Effective(records);
        var completedDefinitions = records.Select(static record => record.DefinitionCode)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requiredInspections = plan.Items.Where(static item => item.Required).ToArray();
        var recordsByDefinition = records
            .GroupBy(static record => (record.DefinitionCode, record.DefinitionVersion))
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(item => item.MeasuredAt).First());
        var missingDefinitions = requiredInspections
            .Where(item => !recordsByDefinition.TryGetValue((item.DefinitionCode, item.DefinitionVersion), out var record) ||
                           item.RequiresAttachment && record.Attachments.Count == 0)
            .Select(static item => item.DefinitionCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reviewRecords = requiredInspections
            .Where(static item => item.RequiresReview)
            .Select(item => recordsByDefinition.GetValueOrDefault((item.DefinitionCode, item.DefinitionVersion)))
            .Where(static record => record is not null)
            .Cast<InspectionRecord>()
            .ToArray();
        var visual = reviewRecords
            .OrderByDescending(static record => record.MeasuredAt)
            .FirstOrDefault();
        var review = visual is null ? null : latestReviews.GetValueOrDefault(visual.RecordId);
        var hasPendingReview = reviewRecords.Any(record =>
            !latestReviews.TryGetValue(record.RecordId, out var latest) ||
            latest.Decision != InspectionReviewDecisions.Confirmed);
        var taskStatus = missingDefinitions.Length > 0
            ? records.Count == 0 ? "pending" : "in_progress"
            : hasPendingReview ? "review_pending" : "completed";
        var context = completed.Event.Context;
        return new InspectionTask
        {
            ScopeType = "production-cycle",
            OperationRunId = completed.Event.CorrelationId!,
            WorkpieceId = context.GetValueOrDefault("workpiece_id", completed.Event.Subject.Id),
            MachineId = completed.Event.Subject.Id,
            ProductSeries = context.GetValueOrDefault("product_series", "unknown"),
            InspectionPlanId = plan.PlanId,
            InspectionPlanVersion = plan.Version,
            InspectionPlanName = plan.Name,
            CompletedAt = completed.Event.OccurredAt,
            Status = taskStatus,
            RequiredDefinitionCodes = requiredInspections.Select(static item => item.DefinitionCode).Distinct(StringComparer.Ordinal).ToArray(),
            RequiredInspections = plan.Items,
            CompletedDefinitionCodes = completedDefinitions,
            MissingDefinitionCodes = missingDefinitions,
            VisualInspectionRecordId = visual?.RecordId,
            VisualReviewDecision = review?.Decision
        };
    }

    private InspectionTask BuildTask(
        InspectionScope scope,
        InspectionPlan plan,
        IReadOnlyList<InspectionRecord> records,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews)
    {
        records = InspectionRecordSet.Effective(records);
        var requiredInspections = plan.Items.Where(static item => item.Required).ToArray();
        var recordsByDefinition = records
            .GroupBy(static record => (record.DefinitionCode, record.DefinitionVersion))
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(item => item.MeasuredAt).First());
        var missingDefinitions = requiredInspections
            .Where(item => !recordsByDefinition.TryGetValue((item.DefinitionCode, item.DefinitionVersion), out var record) ||
                           item.RequiresAttachment && record.Attachments.Count == 0)
            .Select(static item => item.DefinitionCode).Distinct(StringComparer.Ordinal).ToArray();
        var reviewRecords = requiredInspections.Where(static item => item.RequiresReview)
            .Select(item => recordsByDefinition.GetValueOrDefault((item.DefinitionCode, item.DefinitionVersion)))
            .Where(static record => record is not null).Cast<InspectionRecord>().ToArray();
        var visual = reviewRecords.OrderByDescending(static record => record.MeasuredAt).FirstOrDefault();
        var review = visual is null ? null : latestReviews.GetValueOrDefault(visual.RecordId);
        var hasPendingReview = reviewRecords.Any(record =>
            !latestReviews.TryGetValue(record.RecordId, out var latest) || latest.Decision != InspectionReviewDecisions.Confirmed);
        var taskStatus = missingDefinitions.Length > 0
            ? records.Count == 0 ? "pending" : "in_progress"
            : hasPendingReview ? "review_pending" : "completed";
        return new InspectionTask
        {
            ScopeType = scope.ScopeType,
            OperationRunId = scope.ScopeId,
            WorkpieceId = scope.WorkpieceId,
            MachineId = scope.SubjectId,
            ProductSeries = scope.ProductSeries,
            InspectionPlanId = plan.PlanId,
            InspectionPlanVersion = plan.Version,
            InspectionPlanName = plan.Name,
            CompletedAt = scope.To,
            Status = taskStatus,
            RequiredDefinitionCodes = requiredInspections.Select(static item => item.DefinitionCode).Distinct(StringComparer.Ordinal).ToArray(),
            RequiredInspections = plan.Items,
            CompletedDefinitionCodes = records.Select(static item => item.DefinitionCode).Distinct(StringComparer.Ordinal).ToArray(),
            MissingDefinitionCodes = missingDefinitions,
            VisualInspectionRecordId = visual?.RecordId,
            VisualReviewDecision = review?.Decision
        };
    }

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        CancellationToken ct)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var page = await events.QueryAsync(query with { AfterIngestId = cursor, Limit = 500 }, ct)
                .ConfigureAwait(false);
            if (page.Count == 0)
                break;
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("待检任务查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }
}
