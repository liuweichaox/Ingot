using Ingot.Platform.Application.ProcessExecutions;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>
/// PostgreSQL 实现的运行边界存储。
/// </summary>
public sealed class PostgresExecutionBoundaryStore : IExecutionBoundaryStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresExecutionBoundaryStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<ExecutionBoundary?> GetBoundaryAsync(
        string siteId,
        string sourceExecutionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                execution_id, site_id, edge_id, source_execution_id,
                started_at, ended_at, status, event_count, min_ingest_id, max_ingest_id,
                confidence, confidence_reason, last_observed_at, created_at, updated_at
            FROM process_execution_boundaries
            WHERE site_id = $1 AND source_execution_id = $2
            ORDER BY created_at DESC
            LIMIT 1
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(siteId);
        command.Parameters.AddWithValue(sourceExecutionId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return ReadBoundary(reader);
    }

    public async Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO process_execution_boundaries
            (execution_id, site_id, edge_id, source_execution_id, started_at, ended_at,
             status, event_count, min_ingest_id, max_ingest_id, confidence, confidence_reason,
             last_observed_at, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15)
            ON CONFLICT (execution_id) DO NOTHING
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(boundary.ExecutionId);
        command.Parameters.AddWithValue(boundary.SiteId);
        command.Parameters.AddWithValue(boundary.EdgeId);
        command.Parameters.AddWithValue(boundary.SourceExecutionId);
        command.Parameters.AddWithValue(boundary.StartedAt);
        command.Parameters.AddWithValue(boundary.EndedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue((int)boundary.Status);
        command.Parameters.AddWithValue(boundary.EventCount);
        command.Parameters.AddWithValue(boundary.MinIngestId);
        command.Parameters.AddWithValue(boundary.MaxIngestId);
        command.Parameters.AddWithValue((int)boundary.Confidence);
        command.Parameters.AddWithValue(boundary.ConfidenceReason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(boundary.LastObservedAt);
        command.Parameters.AddWithValue(boundary.CreatedAt);
        command.Parameters.AddWithValue(boundary.UpdatedAt);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct)
    {
        const string sql = """
            UPDATE process_execution_boundaries
            SET ended_at = $1, status = $2, event_count = $3, max_ingest_id = $4,
                confidence = $5, confidence_reason = $6, last_observed_at = $7, updated_at = $8
            WHERE execution_id = $9
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(boundary.EndedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue((int)boundary.Status);
        command.Parameters.AddWithValue(boundary.EventCount);
        command.Parameters.AddWithValue(boundary.MaxIngestId);
        command.Parameters.AddWithValue((int)boundary.Confidence);
        command.Parameters.AddWithValue(boundary.ConfidenceReason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(boundary.LastObservedAt);
        command.Parameters.AddWithValue(boundary.UpdatedAt);
        command.Parameters.AddWithValue(boundary.ExecutionId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(
        string siteId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var sql = """
            SELECT
                execution_id, site_id, edge_id, source_execution_id,
                started_at, ended_at, status, event_count, min_ingest_id, max_ingest_id,
                confidence, confidence_reason, last_observed_at, created_at, updated_at
            FROM process_execution_boundaries
            WHERE site_id = $1
            """;

        var paramIndex = 2;
        if (from.HasValue)
        {
            sql += $" AND started_at >= ${paramIndex++}";
        }
        if (to.HasValue)
        {
            sql += $" AND started_at <= ${paramIndex++}";
        }

        sql += " ORDER BY started_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(siteId);

        paramIndex = 2;
        if (from.HasValue)
        {
            command.Parameters.AddWithValue(from.Value);
            paramIndex++;
        }
        if (to.HasValue)
        {
            command.Parameters.AddWithValue(to.Value);
            paramIndex++;
        }

        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);

        var boundaries = new List<ExecutionBoundary>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            boundaries.Add(ReadBoundary(reader));
        }

        return boundaries;
    }

    private static ExecutionBoundary ReadBoundary(NpgsqlDataReader reader)
    {
        return new ExecutionBoundary
        {
            ExecutionId = reader.GetString(0),
            SiteId = reader.GetString(1),
            EdgeId = reader.GetString(2),
            SourceExecutionId = reader.GetString(3),
            StartedAt = reader.GetFieldValue<DateTimeOffset>(4),
            EndedAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
            Status = (ExecutionBoundaryStatus)reader.GetInt32(6),
            EventCount = reader.GetInt32(7),
            MinIngestId = reader.GetInt64(8),
            MaxIngestId = reader.GetInt64(9),
            Confidence = (ExecutionBoundaryConfidence)reader.GetInt32(10),
            ConfidenceReason = reader.IsDBNull(11) ? null : reader.GetString(11),
            LastObservedAt = reader.GetFieldValue<DateTimeOffset>(12),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(13),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(14)
        };
    }
}
