using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Edge.Agent.HealthChecks;
using Ingot.Infrastructure.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class EventLogHealthCheckTests
{
    [Fact]
    public async Task CheckHealth_ShouldDegradeAfterAppendFailureAndRecoverAfterSuccess()
    {
        var persistence = new EventPersistenceHealth();
        var check = new EventLogHealthCheck(new AvailableEventLog(12), persistence);
        var context = new HealthCheckContext();

        persistence.ReportFailure(
            DateTimeOffset.Parse("2026-07-17T01:00:00Z"),
            new IOException("disk full"));
        var degraded = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Degraded, degraded.Status);
        Assert.Equal(1, degraded.Data["consecutivePersistenceFailures"]);
        Assert.Equal("disk full", degraded.Data["lastPersistenceError"]);
        Assert.Equal(12L, degraded.Data["pendingEvents"]);

        persistence.ReportSuccess(DateTimeOffset.Parse("2026-07-17T01:01:00Z"));
        var healthy = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Equal(0, healthy.Data["consecutivePersistenceFailures"]);
        Assert.DoesNotContain("lastPersistenceError", healthy.Data.Keys);
    }

    private sealed class AvailableEventLog(long pending) : IEventLog
    {
        public Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductionEvent>> QueryAsync(
            EventQuery query,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(
            int max,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task MarkShippedAsync(long upToSeq, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task IncrementShipAttemptsAsync(
            long fromSeq,
            long toSeq,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => Task.FromResult(pending);
    }
}
