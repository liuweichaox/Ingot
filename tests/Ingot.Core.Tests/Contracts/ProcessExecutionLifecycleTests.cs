// Cover partial capture and legacy event compatibility at the shared recommendation admission boundary.
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class ProcessExecutionLifecycleTests
{
    [Fact]
    public void LegacyAndExplicitCompletion_AreComplete()
    {
        var (started, completed) = Events();
        Assert.True(ProcessExecutionLifecycle.IsComplete(started, completed));
        Assert.True(ProcessExecutionLifecycle.IsComplete(started,
            completed with { Data = new Dictionary<string, object?> { ["completionStatus"] = "completed" } }));
        Assert.True(ProcessExecutionLifecycle.IsComplete(started,
            completed with { Data = new Dictionary<string, object?> { ["completionStatus"] = JsonSerializer.SerializeToElement("completed") } }));
    }

    [Theory]
    [InlineData("partial_after_connector_start")]
    [InlineData("aborted")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void PartialOrUnknownCompletion_IsRejected(string? status)
    {
        var (started, completed) = Events();
        Assert.False(ProcessExecutionLifecycle.IsComplete(started,
            completed with { Data = new Dictionary<string, object?> { ["completionStatus"] = status } }));
    }

    [Fact]
    public void ObservedActiveAtStartup_RemainsIncompleteEvenWithCompletionEvent()
    {
        var (started, completed) = Events();
        var partial = started with
        {
            Data = new Dictionary<string, object?> { ["lifecycleCaptureStatus"] = "active_at_connector_start" }
        };
        Assert.False(ProcessExecutionLifecycle.IsComplete(partial, completed));
        Assert.False(ProcessExecutionLifecycle.IsComplete(started, null));
        Assert.False(ProcessExecutionLifecycle.IsComplete(null, completed));
        Assert.False(ProcessExecutionLifecycle.IsComplete(started, completed with { OccurredAt = started.OccurredAt.AddSeconds(-1) }));
    }

    private static (ProductionEvent Started, ProductionEvent Completed) Events()
    {
        var start = DateTimeOffset.Parse("2026-10-27T01:00:00Z");
        var subject = new ObjectRef("equipment", "UNIT-01");
        return (
            ProductionEvent.Create("process.execution.started", start, "edge/EDGE-1/press", subject, "run-1"),
            ProductionEvent.Create("process.execution.completed", start.AddMinutes(5), "edge/EDGE-1/press", subject, "run-1"));
    }
}
