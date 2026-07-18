using Ingot.Central.Infrastructure.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ingot.Central.Api.HealthChecks;

public sealed class PostgresEventStoreHealthCheck(ICentralEventStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await store.CanConnectAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy("PostgreSQL event store available")
            : HealthCheckResult.Unhealthy("PostgreSQL event store unavailable");
}
