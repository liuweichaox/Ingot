// 验证平台组件 PlatformWorkerRegistration 的成功、拒绝和安全边界。

using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.Identity;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Ingot.Platform.Infrastructure.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PlatformWorkerRegistrationTests
{
    [Fact]
    public async Task ApiInfrastructure_ShouldShareOneDataSourceAndExcludeMutatingWorkers()
    {
        var services = BuildServices();
        services.AddIngotLocalIdentity(BuildConfiguration());
        await using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<NpgsqlDataSource>(),
            provider.GetRequiredService<NpgsqlDataSource>());
        var hosted = provider.GetServices<IHostedService>().Select(static value => value.GetType()).ToArray();
        Assert.DoesNotContain(typeof(KnowledgeExtractionWorker), hosted);
        Assert.DoesNotContain(typeof(ProcessExecutionAnalysisRecomputeHostedService), hosted);
        Assert.DoesNotContain(typeof(ProcessExecutionAnalysisBackfillService), hosted);
        Assert.DoesNotContain(typeof(SessionPruneHostedService), hosted);
    }

    [Fact]
    public async Task WorkerRegistration_ShouldAddMutatingWorkers()
    {
        var services = BuildServices();
        services.AddIngotPlatformWorkers(BuildConfiguration());
        services.AddIngotLocalIdentityMaintenance();
        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().Select(static value => value.GetType()).ToArray();
        Assert.Contains(typeof(KnowledgeExtractionWorker), hosted);
        Assert.Contains(typeof(ProcessExecutionAnalysisRecomputeHostedService), hosted);
        Assert.Contains(typeof(ProcessExecutionAnalysisBackfillService), hosted);
        Assert.Contains(typeof(SessionPruneHostedService), hosted);
        Assert.Contains(typeof(PlatformWorkerPulseHostedService), hosted);
        Assert.Same(
            provider.GetRequiredService<PlatformWorkerPulse>(),
            provider.GetRequiredService<PlatformWorkerPulse>());
    }

    [Fact]
    public async Task WorkerPulseHealth_ShouldRejectMissingAndStaleHeartbeat()
    {
        var time = new TestTimeProvider(DateTimeOffset.Parse("2026-08-26T00:00:00Z"));
        var pulse = new PlatformWorkerPulse(time);
        var options = Microsoft.Extensions.Options.Options.Create(new PlatformWorkerPulseOptions
        {
            Interval = TimeSpan.FromSeconds(5),
            StaleAfter = TimeSpan.FromSeconds(30)
        });
        var health = new PlatformWorkerPulseHealthCheck(pulse, options);

        Assert.Equal(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            (await health.CheckHealthAsync(new())).Status);

        pulse.RecordHeartbeat();
        Assert.Equal(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy,
            (await health.CheckHealthAsync(new())).Status);
        Assert.Contains(
            "platform_worker_heartbeat_timestamp_seconds",
            pulse.RenderPrometheus(options.Value.StaleAfter));

        time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            (await health.CheckHealthAsync(new())).Status);
    }

    private static ServiceCollection BuildServices()
    {
        var configuration = BuildConfiguration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIngotPlatformInfrastructure(configuration);
        services.AddIngotInspectionInfrastructure(configuration);
        return services;
    }

    private static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Events"] = "Host=localhost;Database=ingot;Username=ingot;Password=test",
                ["ProcessOptimizer:BaseUrl"] = "http://localhost:8100"
            })
            .Build();

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
