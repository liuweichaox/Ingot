using Ingot.Domain.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform.ProcessExecutions;

/// <summary>
/// 运行边界识别器的单元测试。
/// </summary>
public class ExecutionBoundaryRecognizerTests
{
    private readonly IExecutionBoundaryRecognizer _recognizer = new ExecutionBoundaryRecognizer();

    [Fact]
    public async Task RecognizeBoundaries_WithExplicitStartEnd_CreatesCompleteConfidence()
    {
        // Arrange
        var siteId = "site-001";
        var edgeId = "edge-001";
        var executionId = "exec-123";
        var baseTime = DateTimeOffset.UtcNow;

        var events = new List<ProductionEvent>
        {
            CreateEvent("process.execution.started", baseTime, executionId),
            CreateEvent("process.parameter.set", baseTime.AddSeconds(1), executionId),
            CreateEvent("process.execution.ended", baseTime.AddSeconds(10), executionId),
        };

        var options = new ExecutionBoundaryRecognitionOptions();

        // Act
        var boundaries = await _recognizer.RecognizeBoundariesAsync(siteId, edgeId, events, options, CancellationToken.None);

        // Assert
        Assert.Single(boundaries);
        var boundary = boundaries.First();
        Assert.Equal(executionId, boundary.SourceExecutionId);
        Assert.Equal(baseTime, boundary.StartedAt);
        Assert.Equal(baseTime.AddSeconds(10), boundary.EndedAt);
        Assert.Equal(ExecutionBoundaryStatus.Completed, boundary.Status);
        Assert.Equal(ExecutionBoundaryConfidence.Complete, boundary.Confidence);
        Assert.Equal(3, boundary.EventCount);
    }

    [Fact]
    public async Task RecognizeBoundaries_WithoutStartEvent_UseFirstEventTime()
    {
        // Arrange
        var siteId = "site-001";
        var edgeId = "edge-001";
        var executionId = "exec-124";
        var baseTime = DateTimeOffset.UtcNow;

        var events = new List<ProductionEvent>
        {
            CreateEvent("process.parameter.set", baseTime, executionId),
            CreateEvent("process.execution.ended", baseTime.AddSeconds(5), executionId),
        };

        var options = new ExecutionBoundaryRecognitionOptions();

        // Act
        var boundaries = await _recognizer.RecognizeBoundariesAsync(siteId, edgeId, events, options, CancellationToken.None);

        // Assert
        Assert.Single(boundaries);
        var boundary = boundaries.First();
        Assert.Equal(baseTime, boundary.StartedAt); // 用第一个事件时间
        Assert.Equal(ExecutionBoundaryConfidence.Fragmented, boundary.Confidence);
        Assert.Contains("process.execution.started", boundary.ConfidenceReason ?? "");
    }

    [Fact]
    public async Task RecognizeBoundaries_WithMultipleExecutionIds_CreatesSeparateBoundaries()
    {
        // Arrange
        var siteId = "site-001";
        var edgeId = "edge-001";
        var baseTime = DateTimeOffset.UtcNow;

        var events = new List<ProductionEvent>
        {
            CreateEvent("process.execution.started", baseTime, "exec-1"),
            CreateEvent("process.execution.ended", baseTime.AddSeconds(5), "exec-1"),
            CreateEvent("process.execution.started", baseTime.AddSeconds(10), "exec-2"),
            CreateEvent("process.execution.ended", baseTime.AddSeconds(15), "exec-2"),
        };

        var options = new ExecutionBoundaryRecognitionOptions();

        // Act
        var boundaries = await _recognizer.RecognizeBoundariesAsync(siteId, edgeId, events, options, CancellationToken.None);

        // Assert
        Assert.Equal(2, boundaries.Count);
        Assert.Equal("exec-1", boundaries[0].SourceExecutionId);
        Assert.Equal("exec-2", boundaries[1].SourceExecutionId);
    }

    [Fact]
    public void AdjustForLateArrival_EventBelongsToExistingExecution_UpdatesBoundary()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow;
        var boundary = new ExecutionBoundary
        {
            ExecutionId = "boundary-1",
            SiteId = "site-001",
            EdgeId = "edge-001",
            SourceExecutionId = "exec-123",
            StartedAt = baseTime,
            EndedAt = baseTime.AddSeconds(10),
            Status = ExecutionBoundaryStatus.Completed,
            EventCount = 2,
            MinIngestId = 1,
            MaxIngestId = 2,
            LastObservedAt = baseTime.AddSeconds(10),
            CreatedAt = baseTime,
            UpdatedAt = baseTime
        };

        var lateEvent = CreateEvent("process.parameter.set", baseTime.AddSeconds(9), "exec-123");
        var options = new ExecutionBoundaryRecognitionOptions();

        // Act
        var adjustment = _recognizer.AdjustForLateArrival(boundary, lateEvent, options);

        // Assert
        Assert.NotNull(adjustment.AdjustedExisting);
        Assert.Null(adjustment.NewBoundary);
        Assert.Equal(3, adjustment.AdjustedExisting.EventCount); // 事件计数增加
    }

    [Fact]
    public void AdjustForLateArrival_EventOutsideThreshold_CreateNewBoundary()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow;
        var boundary = new ExecutionBoundary
        {
            ExecutionId = "boundary-1",
            SiteId = "site-001",
            EdgeId = "edge-001",
            SourceExecutionId = "exec-123",
            StartedAt = baseTime,
            EndedAt = baseTime.AddSeconds(10),
            Status = ExecutionBoundaryStatus.Completed,
            EventCount = 2,
            MinIngestId = 1,
            MaxIngestId = 2,
            LastObservedAt = baseTime.AddSeconds(10),
            CreatedAt = baseTime,
            UpdatedAt = baseTime
        };

        // 晚到超出阈值（默认 1 小时）
        var lateEvent = CreateEvent("process.parameter.set", baseTime.AddHours(2), "exec-123");
        var options = new ExecutionBoundaryRecognitionOptions();

        // Act
        var adjustment = _recognizer.AdjustForLateArrival(boundary, lateEvent, options);

        // Assert
        Assert.NotNull(adjustment.AdjustedExisting);
        Assert.NotNull(adjustment.NewBoundary);
        Assert.Equal("exec-123", adjustment.NewBoundary!.SourceExecutionId);
        Assert.Equal(ExecutionBoundaryStatus.InProgress, adjustment.NewBoundary.Status);
    }

    [Fact]
    public void MarkGapDetected_UpdatesConfidenceReason()
    {
        // Arrange
        var boundary = new ExecutionBoundary
        {
            ExecutionId = "boundary-1",
            SiteId = "site-001",
            EdgeId = "edge-001",
            SourceExecutionId = "exec-123",
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = null,
            Status = ExecutionBoundaryStatus.InProgress,
            EventCount = 10,
            MinIngestId = 1,
            MaxIngestId = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var marked = _recognizer.MarkGapDetected(boundary, "Seq 从 50 到 80 缺失");

        // Assert
        Assert.Contains("缺口", marked.ConfidenceReason ?? "");
        Assert.Contains("50", marked.ConfidenceReason ?? "");
        Assert.Contains("80", marked.ConfidenceReason ?? "");
    }

    private ProductionEvent CreateEvent(string eventType, DateTimeOffset occurredAt, string? executionId)
    {
        return ProductionEvent.Create(
            eventType,
            occurredAt,
            "edge/EDGE-001/system",
            new ObjectRef("process", "p-001"),
            executionId,
            new Dictionary<string, string> { { "source", "test" } },
            null,
            null,
            null);
    }
}
