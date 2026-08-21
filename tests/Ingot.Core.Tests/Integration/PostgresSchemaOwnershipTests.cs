// 验证 PostgresSchemaOwnership 的真实基础设施集成、失败和恢复行为。

using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresSchemaOwnershipTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task StoreInitializers_AfterMigrations_ShouldNotChangePublicTableColumns()
    {
        await postgres.EnsureSchemaAsync();
        var storageRoot = Path.Combine(Path.GetTempPath(), $"ingot-schema-ownership-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Events"] = postgres.ConnectionString,
                    ["InspectionAttachments:RootPath"] = Path.Combine(storageRoot, "inspections"),
                    ["ProcessKnowledge:RootPath"] = Path.Combine(storageRoot, "knowledge"),
                    ["EventIngest:RetentionDays"] = "0",
                })
                .Build();
            var before = await ReadPublicColumnsAsync(postgres.ConnectionString);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddIngotPlatformInfrastructure(configuration);
            services.AddIngotInspectionInfrastructure(configuration);
            await using var provider = services.BuildServiceProvider();

            await provider.GetRequiredService<IManufacturingContextStore>().InitializeAsync();
            await provider.GetRequiredService<IProcessExecutionAnalysisMaterializationStore>().InitializeAsync();
            await provider.GetRequiredService<PostgresTimeSeriesStore>().InitializeAsync();
            await provider.GetRequiredService<IPlatformEventStore>().InitializeAsync();
            await provider.GetRequiredService<IInspectionRecordStore>().InitializeAsync();
            await provider.GetRequiredService<IInspectionAttachmentStore>().InitializeAsync();
            await provider.GetRequiredService<IInspectionMasterDataStore>().InitializeAsync();
            await provider.GetRequiredService<IInspectionReviewStore>().InitializeAsync();
            await provider.GetRequiredService<IProcessConfigurationStore>().InitializeAsync();
            await provider.GetRequiredService<IResearchAssetStore>().InitializeAsync();
            await provider.GetRequiredService<IIngestionTaskStore>().InitializeAsync();

            var after = await ReadPublicColumnsAsync(postgres.ConnectionString);
            Assert.Equal(before, after);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
                Directory.Delete(storageRoot, recursive: true);
        }
    }

    private static async Task<string[]> ReadPublicColumnsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name, ordinal_position, column_name, data_type, udt_name, is_nullable,
                   COALESCE(column_default, '')
            FROM information_schema.columns
            WHERE table_schema = 'public'
            ORDER BY table_name, ordinal_position;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(string.Join('|',
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return rows.ToArray();
    }
}
