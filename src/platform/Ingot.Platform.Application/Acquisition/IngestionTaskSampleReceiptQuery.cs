// Resolves task-version receipt evidence from the event or typed-sample persistence path.
using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.TimeSeries;

namespace Ingot.Platform.Application.Acquisition;

/// <summary>Reads the latest persisted sample evidence for a specific task version.</summary>
public sealed class IngestionTaskSampleReceiptQuery(
    ITimeSeriesStore timeSeries,
    IPlatformEventStore events)
{
    public async Task<IngestionTaskSampleReceipt> ExecuteAsync(
        IngestionTask task,
        string siteId,
        string? eventId = null,
        CancellationToken ct = default)
    {
        if (string.Equals(task.SampleEventType, "process.sample", StringComparison.Ordinal))
        {
            var frames = await timeSeries.QueryFramesAsync(new TimeSeriesQuery
            {
                SiteId = siteId,
                EdgeId = task.EdgeId,
                EventId = eventId,
                SubjectType = task.SubjectType,
                SubjectId = task.SubjectId,
                AppliedConfiguration = new AppliedConfigurationRef("ingestion-task", task.TaskId, task.Version),
                OrderByIngestedAtDescending = true,
                Limit = 1
            }, ct).ConfigureAwait(false);
            var frame = frames.FirstOrDefault();
            return new IngestionTaskSampleReceipt(
                frame?.EventId,
                frame?.IngestedAt,
                frame is null ? null : "typed-sample-frame");
        }

        var matchingEvents = await events.QueryAsync(new PlatformEventQuery
        {
            SiteId = siteId,
            EdgeId = task.EdgeId,
            EventId = eventId,
            EventType = task.SampleEventType,
            AppliedConfigurationKind = "ingestion-task",
            AppliedConfigurationId = task.TaskId,
            AppliedConfigurationVersion = task.Version,
            SubjectType = task.SubjectType,
            SubjectId = task.SubjectId,
            RequireNonEmptyDataValues = true,
            OrderByIngestedAtDescending = true,
            Limit = 1
        }, ct).ConfigureAwait(false);
        var latestEvent = matchingEvents.FirstOrDefault();
        return new IngestionTaskSampleReceipt(
            latestEvent?.Event.EventId,
            latestEvent?.IngestedAt,
            latestEvent is null ? null : "event-payload");
    }
}

/// <summary>Describes the matched sample event and its Platform receipt time.</summary>
public sealed record IngestionTaskSampleReceipt(
    string? EventId,
    DateTimeOffset? IngestedAt,
    string? EvidenceKind);
