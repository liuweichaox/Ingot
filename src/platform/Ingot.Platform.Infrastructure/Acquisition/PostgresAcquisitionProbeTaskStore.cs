using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Acquisition;

public sealed class PostgresAcquisitionProbeTaskStore(NpgsqlDataSource dataSource) : IAcquisitionProbeTaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(30);

    public async Task EnqueueAsync(AcquisitionProbeTask task, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO acquisition_probe_tasks(
              task_id, edge_id, expected_protocol, task_payload, status, created_at, expires_at)
            VALUES (@task_id, @edge_id, @protocol, @payload, 'queued', @created_at, @expires_at);
            """);
        command.Parameters.AddWithValue("task_id", task.TaskId);
        command.Parameters.AddWithValue("edge_id", task.EdgeId);
        command.Parameters.AddWithValue("protocol", task.Deployment.Task.Protocol);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(task, JsonOptions));
        command.Parameters.AddWithValue("created_at", task.CreatedAt);
        command.Parameters.AddWithValue("expires_at", task.ExpiresAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<AcquisitionProbeTask?> ClaimNextAsync(string edgeId, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH candidate AS (
              SELECT task_id FROM acquisition_probe_tasks
              WHERE edge_id = @edge_id AND expires_at > now()
                AND (status = 'queued' OR (status = 'claimed' AND claimed_at < now() - @claim_lease))
              ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE acquisition_probe_tasks task
            SET status = 'claimed', claimed_at = now()
            FROM candidate WHERE task.task_id = candidate.task_id
            RETURNING task.task_payload::text;
            """);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("claim_lease", ClaimLease);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return payload is null ? null : JsonSerializer.Deserialize<AcquisitionProbeTask>(payload, JsonOptions);
    }

    public async Task<bool> CompleteAsync(AcquisitionProbeTaskCompletion completion, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE acquisition_probe_tasks
            SET status = 'completed', result_payload = @result, completed_at = now()
            WHERE task_id = @task_id AND edge_id = @edge_id AND status = 'claimed'
              AND expires_at > now() AND expected_protocol = @protocol;
            """);
        command.Parameters.AddWithValue("task_id", completion.TaskId);
        command.Parameters.AddWithValue("edge_id", completion.EdgeId);
        command.Parameters.AddWithValue("protocol", completion.Result.Protocol);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(completion.Result, JsonOptions));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<AcquisitionProbeResult?> GetResultAsync(string taskId, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT result_payload::text FROM acquisition_probe_tasks WHERE task_id = @task_id AND status = 'completed';");
        command.Parameters.AddWithValue("task_id", taskId);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return payload is null ? null : JsonSerializer.Deserialize<AcquisitionProbeResult>(payload, JsonOptions);
    }

    public async Task DeleteAsync(string taskId, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM acquisition_probe_tasks WHERE task_id = @task_id;");
        command.Parameters.AddWithValue("task_id", taskId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
