using Ingot.Edge.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ingot.Edge.ConnectorHost.HealthChecks;

public sealed class EventLogHealthCheck(
    IEventLog eventLog,
    IEventPersistenceHealth persistenceHealth) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = await eventLog.CountPendingAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = persistenceHealth.Snapshot;
            var data = new Dictionary<string, object>
            {
                ["pendingEvents"] = pending,
                ["consecutivePersistenceFailures"] = snapshot.ConsecutiveFailures
            };
            if (snapshot.LastSuccessAt.HasValue)
                data["lastPersistenceSuccessAt"] = snapshot.LastSuccessAt.Value;
            if (snapshot.LastFailureAt.HasValue)
                data["lastPersistenceFailureAt"] = snapshot.LastFailureAt.Value;
            if (!string.IsNullOrWhiteSpace(snapshot.LastError))
                data["lastPersistenceError"] = snapshot.LastError;

            if (snapshot.IsDegraded)
            {
                return HealthCheckResult.Degraded(
                    "event log append path failed; waiting for a successful persisted event",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                "event log available",
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("event log unavailable", ex);
        }
    }
}
