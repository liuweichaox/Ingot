// 实现 PostgresEventStoreHealthCheck 的 PostgreSQL 持久化适配，避免数据库细节泄漏到应用层。

using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Application.Events;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ingot.Platform.Api.HealthChecks;

public sealed class PostgresPlatformEventStoreHealthCheck(IPlatformEventStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => await store.CanConnectAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy("PostgreSQL event store available")
            : HealthCheckResult.Unhealthy("PostgreSQL event store unavailable");
}
