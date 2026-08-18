using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
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
        await using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<NpgsqlDataSource>(),
            provider.GetRequiredService<NpgsqlDataSource>());
        var hosted = provider.GetServices<IHostedService>().Select(static value => value.GetType()).ToArray();
        Assert.DoesNotContain(typeof(KnowledgeExtractionWorker), hosted);
        Assert.DoesNotContain(typeof(ProcessExecutionAnalysisRecomputeHostedService), hosted);
        Assert.DoesNotContain(typeof(ProcessExecutionAnalysisBackfillService), hosted);
        Assert.DoesNotContain(typeof(ResearchExperimentAutomationHostedService), hosted);
    }

    [Fact]
    public async Task WorkerRegistration_ShouldAddMutatingWorkers()
    {
        var services = BuildServices();
        services.AddIngotPlatformWorkers();
        await using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().Select(static value => value.GetType()).ToArray();
        Assert.Contains(typeof(KnowledgeExtractionWorker), hosted);
        Assert.Contains(typeof(ProcessExecutionAnalysisRecomputeHostedService), hosted);
        Assert.Contains(typeof(ProcessExecutionAnalysisBackfillService), hosted);
        Assert.Contains(typeof(ResearchExperimentAutomationHostedService), hosted);
    }

    private static ServiceCollection BuildServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Events"] = "Host=localhost;Database=ingot;Username=ingot;Password=test",
                ["ProcessOptimizer:BaseUrl"] = "http://localhost:8100"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIngotPlatformInfrastructure(configuration);
        return services;
    }
}
