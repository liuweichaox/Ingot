// 验证 PostgresBatchHydrationCommandCount 的真实基础设施集成、失败和恢复行为。

using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresBatchHydrationCommandCountTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ListHypothesesAndClaims_UseConstantCommandCountsFor250Rows()
    {
        await postgres.EnsureSchemaAsync();
        var projectId = Guid.CreateVersion7();
        await SeedAsync(projectId);
        var counter = new CommandCounterProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Debug).AddProvider(counter));
        await using var countedDataSource = new NpgsqlDataSourceBuilder(postgres.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .Build();

        counter.Reset();
        var hypotheses = await new PostgresProcessResearchStore(countedDataSource)
            .ListHypothesesAsync(projectId);
        Assert.Equal(250, hypotheses.Count);
        Assert.InRange(counter.CommandCount, 1, 2);

        counter.Reset();
        var claims = await new PostgresMechanismKnowledgeStore(countedDataSource)
            .ListClaimsAsync(projectId);
        Assert.Equal(250, claims.Count);
        Assert.InRange(counter.CommandCount, 1, 6);
    }

    private async Task SeedAsync(Guid projectId)
    {
        await using var command = postgres.DataSource.CreateCommand(
            """
            INSERT INTO process_research_projects(project_id,code,status,revision,payload,created_at,updated_at)
            VALUES(@project_id,CAST(@project_id AS text),'draft',1,'{}'::jsonb,now(),now());

            INSERT INTO research_hypotheses(
              hypothesis_id,project_id,status,statement,rationale,confidence,created_by,created_at,updated_at)
            SELECT gen_random_uuid(),@project_id,'proposed','statement-'||value,'rationale',0,'tester',now(),now()
            FROM generate_series(1,250) value;

            WITH inserted AS (
              INSERT INTO mechanism_claims(claim_id,project_id,current_version,status,created_at,updated_at)
              SELECT gen_random_uuid(),@project_id,1,'draft',now(),now()
              FROM generate_series(1,250)
              RETURNING claim_id)
            INSERT INTO mechanism_claim_versions(
              claim_id,version,name,mechanism_type,statement,falsification_condition,
              evidence_level,created_by,created_at,content_hash)
            SELECT claim_id,1,'claim','qualitative','statement','falsification',
              'engineering-observation','tester',now(),md5(claim_id::text)||md5(claim_id::text)
            FROM inserted;
            """);
        command.Parameters.AddWithValue("project_id", projectId);
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
