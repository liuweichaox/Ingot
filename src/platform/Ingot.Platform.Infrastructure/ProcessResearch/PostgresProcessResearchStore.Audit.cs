// 持久化并分页读取研究审计记录。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
    public async Task AddAuditEntryAsync(
        ResearchAuditEntry value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_research_audit
              (entry_id, project_id, resource_type, resource_id, action, payload, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            """);
        command.Parameters.AddWithValue(value.EntryId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.ResourceType);
        command.Parameters.AddWithValue(value.ResourceId);
        command.Parameters.AddWithValue(value.Action);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ResearchAuditEntry>> ListAuditEntriesAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchAuditEntry>(
            """
            SELECT payload
            FROM process_research_audit
            WHERE project_id = $1
            ORDER BY created_at DESC, entry_id
            """,
            projectId,
            ct);

    public Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchAuditEntry>(
            """
            SELECT payload
            FROM process_research_audit
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (created_at, entry_id) < ($2, $3))
            ORDER BY created_at DESC, entry_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.CreatedAt,
            static value => value.EntryId,
            ct);

}
