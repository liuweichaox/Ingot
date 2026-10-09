// 验证 PostgresBatchHydrationCommandCount 的真实基础设施集成、失败和恢复行为。

using Ingot.Platform.Infrastructure.ResearchAssets;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresBatchHydrationCommandCountTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ListClaims_UsesConstantCommandCountFor250Rows()
    {
        await postgres.EnsureSchemaAsync();
        var siteCode = $"site-{Guid.NewGuid():N}";
        const string specificationId = "hydration-spec";
        await SeedAsync(siteCode, specificationId);
        var counter = new CommandCounterProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Debug).AddProvider(counter));
        await using var countedDataSource = new NpgsqlDataSourceBuilder(postgres.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .Build();

        counter.Reset();
        var claims = await new PostgresMechanismKnowledgeStore(countedDataSource)
            .ListClaimsAsync(siteCode, specificationId);
        Assert.Equal(250, claims.Count);
        Assert.All(claims, claim => Assert.Equal(siteCode, claim.SiteCode));
        Assert.InRange(counter.CommandCount, 1, 6);
    }

    private async Task SeedAsync(string siteCode, string specificationId)
    {
        await using var command = postgres.DataSource.CreateCommand(
            """
            WITH inserted AS (
              INSERT INTO mechanism_claims(
                claim_id,site_code,process_specification_id,current_version,status,created_at,updated_at)
              SELECT gen_random_uuid(),@site_code,@process_specification_id,1,'draft',now(),now()
              FROM generate_series(1,250)
              RETURNING claim_id)
            INSERT INTO mechanism_claim_versions(
              claim_id,version,name,mechanism_type,statement,falsification_condition,
              evidence_level,created_by,created_at,content_hash)
            SELECT claim_id,1,'claim','qualitative','statement','falsification',
              'engineering-observation','tester',now(),md5(claim_id::text)||md5(claim_id::text)
            FROM inserted;
            """);
        command.Parameters.AddWithValue("site_code", siteCode);
        command.Parameters.AddWithValue("process_specification_id", specificationId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class CommandCounterProvider : ILoggerProvider
    {
        private int commandCount;
        public int CommandCount => Volatile.Read(ref commandCount);
        public ILogger CreateLogger(string categoryName) => new CounterLogger(this, categoryName);
        public void Dispose() { }
        public void Reset() => Volatile.Write(ref commandCount, 0);

        private sealed class CounterLogger(CommandCounterProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => category.StartsWith("Npgsql.Command", StringComparison.Ordinal);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && formatter(state, exception).StartsWith("Executing command", StringComparison.Ordinal))
                    Interlocked.Increment(ref owner.commandCount);
            }
        }
    }
}
