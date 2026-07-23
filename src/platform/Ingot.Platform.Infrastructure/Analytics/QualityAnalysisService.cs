using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;

namespace Ingot.Platform.Infrastructure.Analytics;

public sealed class QualityAnalysisService(
    IInspectionRecordStore inspections,
    IPlatformEventStore events) : IQualityAnalysisService
{
    public async Task<QualityAnalysisPage> QueryAsync(
        QualityAnalysisQuery query,
        CancellationToken ct = default)
    {
        var records = InspectionRecordSet.Effective(await QueryAllRecordsAsync(query, ct).ConfigureAwait(false));
        var scopes = (await inspections.ListScopesAsync(ct).ConfigureAwait(false))
            .ToDictionary(static scope => scope.ScopeId, StringComparer.Ordinal);
        var operationIds = records
            .Select(static record => record.OperationRunId)
            .Where(id => !scopes.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var operationEvents = await events.QueryByCorrelationIdsAsync(operationIds, ct).ConfigureAwait(false);
        var eventsByOperation = operationEvents
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.CorrelationId))
            .GroupBy(static row => row.Event.CorrelationId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<PlatformProductionEvent>)group.ToArray(),
                StringComparer.Ordinal);

        var rows = records
            .Select(record => BuildRow(
                record,
                scopes.GetValueOrDefault(record.OperationRunId),
                eventsByOperation.GetValueOrDefault(record.OperationRunId, [])))
            .Where(row => Matches(row, query))
            .OrderByDescending(static row => row.MeasuredAt)
            .ThenBy(static row => row.RecordId)
            .ToArray();
        return new QualityAnalysisPage
        {
            Data = rows.Skip(query.Offset).Take(query.Limit).ToArray(),
            Total = rows.Length,
            Limit = query.Limit,
            Offset = query.Offset
        };
    }

    private async Task<IReadOnlyList<InspectionRecord>> QueryAllRecordsAsync(
        QualityAnalysisQuery query,
        CancellationToken ct)
    {
        var result = new List<InspectionRecord>();
        var offset = 0;
        while (true)
        {
            var page = await inspections.QueryPageAsync(new InspectionRecordQuery
            {
                From = query.From,
                To = query.To,
                Limit = 500,
                Offset = offset
            }, ct).ConfigureAwait(false);
            result.AddRange(page.Data);
            offset += page.Data.Count;
            if (offset >= page.Total || page.Data.Count == 0)
                break;
        }
        return result;
    }

    private static QualityAnalysisRecord BuildRow(
        InspectionRecord record,
        InspectionScope? scope,
        IReadOnlyList<PlatformProductionEvent> operationEvents)
    {
        if (scope is not null)
        {
            return new QualityAnalysisRecord
            {
                RecordId = record.RecordId,
                AnalysisScopeId = scope.ScopeId,
                AnalysisScopeType = scope.ScopeType,
                SubjectType = scope.SubjectType,
                SubjectId = scope.SubjectId,
                QualityObjectId = record.WorkpieceId,
                ProductSeries = scope.ProductSeries,
                ProductCode = Read(scope.Context, "product_code"),
                RecipeId = Read(scope.Context, "recipe_id"),
                RecipeVersion = Read(scope.Context, "recipe_version"),
                DefinitionCode = record.DefinitionCode,
                DefinitionVersion = record.DefinitionVersion,
                ScopeFrom = scope.From,
                ScopeTo = scope.To,
                MeasuredAt = record.MeasuredAt,
                Outcome = record.Outcome,
                MeasurementCount = record.Measurements.Count,
                AttachmentCount = record.Attachments.Count,
                Context = scope.Context
            };
        }

        var ordered = operationEvents
            .OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId)
            .ToArray();
        var first = ordered.FirstOrDefault();
        var context = ordered
            .Select(static row => row.Event.Context)
            .FirstOrDefault(static value => value.Count > 0)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new QualityAnalysisRecord
        {
            RecordId = record.RecordId,
            AnalysisScopeId = record.OperationRunId,
            AnalysisScopeType = ResolveScopeType(ordered),
            SubjectType = first?.Event.Subject.Type ?? "operation",
            SubjectId = first?.Event.Subject.Id ?? record.OperationRunId,
            QualityObjectId = record.WorkpieceId,
            ProductSeries = Read(context, "product_series"),
            ProductCode = Read(context, "product_code"),
            RecipeId = Read(context, "recipe_id"),
            RecipeVersion = Read(context, "recipe_version"),
            DefinitionCode = record.DefinitionCode,
            DefinitionVersion = record.DefinitionVersion,
            ScopeFrom = first?.Event.OccurredAt,
            ScopeTo = ordered.LastOrDefault()?.Event.OccurredAt,
            MeasuredAt = record.MeasuredAt,
            Outcome = record.Outcome,
            MeasurementCount = record.Measurements.Count,
            AttachmentCount = record.Attachments.Count,
            Context = context
        };
    }

    private static string ResolveScopeType(IReadOnlyList<PlatformProductionEvent> rows)
    {
        var startedType = rows
            .Select(static row => row.Event.EventType)
            .FirstOrDefault(static type => type.EndsWith(".started", StringComparison.Ordinal));
        return startedType switch
        {
            "cycle.started" => "production-cycle",
            "run.started" => "production-run",
            _ => "operation-run"
        };
    }

    private static bool Matches(QualityAnalysisRecord row, QualityAnalysisQuery query)
        => MatchesText(row.ProductSeries, query.ProductSeries)
           && MatchesText(row.SubjectType, query.SubjectType)
           && MatchesText(row.SubjectId, query.SubjectId)
           && MatchesText(row.Outcome, query.Outcome);

    private static bool MatchesText(string? actual, string? requested)
        => string.IsNullOrWhiteSpace(requested)
           || string.Equals(actual, requested.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Read(IReadOnlyDictionary<string, string> context, string key)
        => context.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

}
