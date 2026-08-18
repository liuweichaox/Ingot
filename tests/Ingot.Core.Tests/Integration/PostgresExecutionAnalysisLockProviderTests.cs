using Ingot.Platform.Infrastructure.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresExecutionAnalysisLockProviderTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task SameMaterializationKey_ShouldSerializeAcrossProviderInstances()
    {
        await postgres.EnsureSchemaAsync();
        var key = new ProcessExecutionAnalysisMaterializationKey(
            $"execution-{Guid.NewGuid():N}",
            "algorithm-v1",
            "model-a",
            1,
            "plan-a",
            1);
        var firstProvider = new PostgresExecutionAnalysisLockProvider(postgres.DataSource);
        var secondProvider = new PostgresExecutionAnalysisLockProvider(postgres.DataSource);
        var first = await firstProvider.AcquireAsync(key);

        var secondTask = secondProvider.AcquireAsync(key);
        await Task.Delay(100);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
