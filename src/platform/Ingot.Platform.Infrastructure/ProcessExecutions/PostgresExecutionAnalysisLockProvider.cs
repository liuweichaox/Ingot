using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public interface IExecutionAnalysisLockProvider
{
    Task<IAsyncDisposable> AcquireAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        CancellationToken ct = default);
}

public sealed class PostgresExecutionAnalysisLockProvider(NpgsqlDataSource dataSource)
    : IExecutionAnalysisLockProvider
{
    public async Task<IAsyncDisposable> AcquireAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        CancellationToken ct = default)
    {

        var lockKey = string.Join('\u001f',
            key.ExecutionId,
            key.AlgorithmVersion,
            key.DataModelId,
            key.DataModelVersion,
            key.AnalysisPlanId,
            key.AnalysisPlanVersion);
        var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_lock(hashtextextended(@key, 0));",
                connection);
            command.Parameters.AddWithValue("key", lockKey);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new Lease(connection, lockKey);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection, string lockKey) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_unlock(hashtextextended(@key, 0));",
                    connection);
                command.Parameters.AddWithValue("key", lockKey);
                await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
