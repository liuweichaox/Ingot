// 验证 PostgresResearchAssetPagination 的真实基础设施集成、失败和恢复行为。

using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresResearchAssetPaginationTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task DatasetKeysetPagination_HandlesEqualTimestampsNewRowsAndInvalidCursor()
    {
        await postgres.EnsureSchemaAsync();
        var root = Path.Combine(Path.GetTempPath(), $"ingot-pagination-{Guid.NewGuid():N}");
        try
        {
            var store = new PostgresResearchAssetStore(
                postgres.DataSource,
                Options.Create(new ProcessKnowledgeOptions { RootPath = root }));
            var prefix = $"page-{Guid.NewGuid():N}";
            var createdAt = DateTimeOffset.UtcNow.AddYears(20);
            for (var index = 0; index < 250; index++)
                await store.AddDatasetAsync(Dataset($"{prefix}-{index:D3}", createdAt));

            var first = await store.ListDatasetsPageAsync(100, null);
            Assert.Equal(100, first.Data.Count);
            Assert.NotNull(first.NextCursor);

            await store.AddDatasetAsync(Dataset($"{prefix}-newer", createdAt.AddMinutes(1)));
            var second = await store.ListDatasetsPageAsync(100, first.NextCursor);
            var third = await store.ListDatasetsPageAsync(100, second.NextCursor);
            var ids = first.Data.Concat(second.Data).Concat(third.Data)
                .Where(value => value.DatasetId.StartsWith(prefix, StringComparison.Ordinal))
                .Select(static value => value.DatasetId)
                .ToArray();

            Assert.Equal(250, ids.Length);
            Assert.Equal(250, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain($"{prefix}-newer", ids);
            Assert.Null(third.NextCursor);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.ListDatasetsPageAsync(100, "not-a-valid-cursor"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static TrainingDatasetVersion Dataset(string id, DateTimeOffset createdAt) => new()
    {
        DatasetId = id,
        Name = id,
        AnalysisPlanId = "analysis",
        DataModelId = "model",
        TargetCode = "quality",
        ContentHash = id,
        CreatedAt = createdAt,
        WindowStart = createdAt.AddHours(-1),
        WindowEnd = createdAt
    };
}
