// 记录 Worker 心跳并向健康检查和 Prometheus 暴露新鲜度。
using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.Workers;

public sealed class PlatformWorkerPulseOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class PlatformWorkerPulse(TimeProvider timeProvider)
{
    private long _lastHeartbeatUtcTicks;

    public DateTimeOffset? LastHeartbeatUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastHeartbeatUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public TimeSpan? Age
    {
        get
        {
            var lastHeartbeat = LastHeartbeatUtc;
            if (lastHeartbeat is null)
                return null;
            var age = timeProvider.GetUtcNow() - lastHeartbeat.Value;
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }
    }

    public void RecordHeartbeat()
        => Interlocked.Exchange(ref _lastHeartbeatUtcTicks, timeProvider.GetUtcNow().UtcTicks);

    public string RenderPrometheus(TimeSpan staleAfter)
    {
        var timestamp = LastHeartbeatUtc?.ToUnixTimeSeconds() ?? 0;
        var age = Age?.TotalSeconds;
        return string.Join('\n',
            "# HELP platform_worker_heartbeat_timestamp_seconds Unix timestamp of the latest worker host heartbeat.",
            "# TYPE platform_worker_heartbeat_timestamp_seconds gauge",
            $"platform_worker_heartbeat_timestamp_seconds {timestamp.ToString(CultureInfo.InvariantCulture)}",
            "# HELP platform_worker_heartbeat_age_seconds Seconds since the latest worker host heartbeat.",
            "# TYPE platform_worker_heartbeat_age_seconds gauge",
            $"platform_worker_heartbeat_age_seconds {(age is null ? "+Inf" : age.Value.ToString("R", CultureInfo.InvariantCulture))}",
            "# HELP platform_worker_heartbeat_stale_after_seconds Configured maximum worker heartbeat age.",
            "# TYPE platform_worker_heartbeat_stale_after_seconds gauge",
            $"platform_worker_heartbeat_stale_after_seconds {staleAfter.TotalSeconds.ToString("R", CultureInfo.InvariantCulture)}",
            string.Empty);
    }
}

public sealed class PlatformWorkerPulseHostedService(
    PlatformWorkerPulse pulse,
    IOptions<PlatformWorkerPulseOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        pulse.RecordHeartbeat();
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                pulse.RecordHeartbeat();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}

public sealed class PlatformWorkerPulseHealthCheck(
    PlatformWorkerPulse pulse,
    IOptions<PlatformWorkerPulseOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var age = pulse.Age;
        if (age is null)
            return Task.FromResult(HealthCheckResult.Unhealthy("Worker heartbeat has not started."));
        if (age > options.Value.StaleAfter)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Worker heartbeat is stale ({age.Value.TotalSeconds:F1}s)."));
        }
        return Task.FromResult(HealthCheckResult.Healthy(
            $"Worker heartbeat age is {age.Value.TotalSeconds:F1}s."));
    }
}
