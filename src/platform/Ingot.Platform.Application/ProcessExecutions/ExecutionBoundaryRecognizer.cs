using System.Collections.Generic;
using System.Linq;
using Ingot.Domain.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

public sealed class ExecutionBoundaryRecognizer : IExecutionBoundaryRecognizer
{
    public Task<IReadOnlyList<ExecutionBoundary>> RecognizeBoundariesAsync(
        string siteId,
        string edgeId,
        IReadOnlyList<ProductionEvent> events,
        ExecutionBoundaryRecognitionOptions options,
        CancellationToken ct)
    {
        if (events.Count == 0)
            return Task.FromResult<IReadOnlyList<ExecutionBoundary>>(Array.Empty<ExecutionBoundary>());

        var boundaries = new List<ExecutionBoundary>();
        var executionGroups = events
            .GroupBy(e => e.ExecutionId ?? $"_implicit_{e.Source}_{e.Subject.Id}")
            .ToList();

        foreach (var group in executionGroups)
        {
            var executionId = group.Key;
            var groupedEvents = group.OrderBy(e => e.Seq).ToList();

            var boundary = RecognizeSingleExecution(
                siteId,
                edgeId,
                executionId,
                groupedEvents,
                options);

            boundaries.Add(boundary);
        }

        return Task.FromResult<IReadOnlyList<ExecutionBoundary>>(
            boundaries.OrderBy(b => b.StartedAt).ToList());
    }

    public ExecutionBoundaryAdjustment AdjustForLateArrival(
        ExecutionBoundary existingBoundary,
        ProductionEvent lateArrivalEvent,
        ExecutionBoundaryRecognitionOptions options)
    {

        var eventExecutionId = lateArrivalEvent.ExecutionId ?? $"_implicit_{lateArrivalEvent.Source}_{lateArrivalEvent.Subject.Id}";

        if (eventExecutionId != existingBoundary.SourceExecutionId)
        {

            var newBoundary = new ExecutionBoundary
            {
                ExecutionId = Guid.CreateVersion7().ToString(),
                SiteId = existingBoundary.SiteId,
                EdgeId = existingBoundary.EdgeId,
                SourceExecutionId = eventExecutionId,
                StartedAt = lateArrivalEvent.OccurredAt,
                EndedAt = null,
                Status = ExecutionBoundaryStatus.InProgress,
                EventCount = 1,
                MinIngestId = long.MaxValue,
                MaxIngestId = long.MinValue,
                Confidence = ExecutionBoundaryConfidence.Fragmented,
                ConfidenceReason = "新的 ExecutionId，从晚到事件识别。",
                LastObservedAt = lateArrivalEvent.RecordedAt,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return new ExecutionBoundaryAdjustment
            {
                AdjustedExisting = existingBoundary,
                NewBoundary = newBoundary
            };
        }

        if (existingBoundary.EndedAt.HasValue &&
            lateArrivalEvent.OccurredAt > existingBoundary.EndedAt.Value + options.LateArrivalThreshold)
        {

            var newBoundary = new ExecutionBoundary
            {
                ExecutionId = Guid.CreateVersion7().ToString(),
                SiteId = existingBoundary.SiteId,
                EdgeId = existingBoundary.EdgeId,
                SourceExecutionId = eventExecutionId,
                StartedAt = lateArrivalEvent.OccurredAt,
                EndedAt = null,
                Status = ExecutionBoundaryStatus.InProgress,
                EventCount = 1,
                MinIngestId = long.MaxValue,
                MaxIngestId = long.MinValue,
                Confidence = ExecutionBoundaryConfidence.Fragmented,
                ConfidenceReason = $"晚到事件超出 {options.LateArrivalThreshold.TotalHours} 小时阈值，分入新运行。",
                LastObservedAt = lateArrivalEvent.RecordedAt,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            return new ExecutionBoundaryAdjustment
            {
                AdjustedExisting = existingBoundary,
                NewBoundary = newBoundary
            };
        }

        var adjusted = existingBoundary with
        {
            EventCount = existingBoundary.EventCount + 1,
            LastObservedAt = lateArrivalEvent.RecordedAt,

            EndedAt = existingBoundary.EndedAt.HasValue
                ? (lateArrivalEvent.OccurredAt > existingBoundary.EndedAt.Value
                    ? lateArrivalEvent.OccurredAt
                    : existingBoundary.EndedAt)
                : null,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return new ExecutionBoundaryAdjustment
        {
            AdjustedExisting = adjusted,
            NewBoundary = null
        };
    }

    public ExecutionBoundary MarkGapDetected(ExecutionBoundary boundary, string gapDescription)
    {
        return boundary with
        {
            GapDetected = true,
            Confidence = ExecutionBoundaryConfidence.Fragmented,
            ConfidenceReason = string.IsNullOrEmpty(boundary.ConfidenceReason)
                ? $"检测到缺口: {gapDescription}"
                : $"{boundary.ConfidenceReason}; 检测到缺口: {gapDescription}",
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private ExecutionBoundary RecognizeSingleExecution(
        string siteId,
        string edgeId,
        string executionId,
        List<ProductionEvent> events,
        ExecutionBoundaryRecognitionOptions options)
    {
        var startEvent = events.FirstOrDefault(e => e.EventType == "process.execution.started");
        var endEvent = events.FirstOrDefault(e =>
            e.EventType == "process.execution.completed");

        DateTimeOffset startedAt;
        DateTimeOffset? endedAt = null;
        ExecutionBoundaryConfidence confidence;
        string? confidenceReason = null;

        if (startEvent is not null)
        {
            startedAt = startEvent.OccurredAt;
        }
        else
        {

            startedAt = events.First().OccurredAt;
            confidenceReason = "无 process.execution.started 事件，用第一条事件时间推断。";
        }

        if (endEvent is not null)
        {
            endedAt = endEvent.OccurredAt;
            confidence = startEvent is not null ? ExecutionBoundaryConfidence.Complete : ExecutionBoundaryConfidence.Fragmented;
        }
        else
        {

            var lastEventTime = events.Max(e => e.OccurredAt);
            var elapsedSinceLastEvent = DateTimeOffset.UtcNow - lastEventTime;

            if (elapsedSinceLastEvent > options.ExecutionTimeoutWithoutEndEvent)
            {

                endedAt = lastEventTime + options.ExecutionTimeoutWithoutEndEvent;
                confidence = ExecutionBoundaryConfidence.InferredEnd;
                confidenceReason = (confidenceReason ?? "") +
                    $"; 无 process.execution.completed 事件，用超时（{options.ExecutionTimeoutWithoutEndEvent.TotalHours} 小时）推断结束。";
            }
            else
            {

                confidence = startEvent is not null ? ExecutionBoundaryConfidence.Fragmented : ExecutionBoundaryConfidence.Fragmented;
                confidenceReason = (confidenceReason ?? "") +
                    "; 无 process.execution.completed 事件，运行状态为 InProgress。";
            }
        }

        var minSeq = events.Min(e => e.Seq);
        var maxSeq = events.Max(e => e.Seq);

        return new ExecutionBoundary
        {
            ExecutionId = Guid.CreateVersion7().ToString(),
            SiteId = siteId,
            EdgeId = edgeId,
            SourceExecutionId = executionId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Status = endedAt.HasValue ? ExecutionBoundaryStatus.Completed : ExecutionBoundaryStatus.InProgress,
            EventCount = events.Count,
            MinIngestId = minSeq,
            MaxIngestId = maxSeq,
            Confidence = confidence,
            ConfidenceReason = confidenceReason?.TrimStart(';').Trim(),
            LastObservedAt = events.Max(e => e.RecordedAt),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
