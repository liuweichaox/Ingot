// 在 PostgreSQL 中持久化研发项目及资产，并执行站点过滤的项目查询。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore : IProcessResearchStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessResearchStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
        => GetOneAsync<ResearchProject>(
            "SELECT payload FROM process_research_projects WHERE project_id = $1",
            projectId,
            ct);

    public Task<ResearchProject?> GetProjectByCodeAsync(
        string code,
        CancellationToken ct = default)
        => GetOneAsync<ResearchProject>(
            "SELECT payload FROM process_research_projects WHERE code = $1",
            code,
            ct);

    public async Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
        string userId,
        bool includeAll,
        IReadOnlyCollection<string>? siteIds,
        int limit,
        int offset,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT project.payload
            FROM process_research_projects project
            WHERE $1 OR EXISTS (
              SELECT 1
              FROM research_project_members member
              WHERE member.project_id = project.project_id
                AND member.user_id = $2
            )
            AND ($1 OR lower(project.payload->>'siteCode') = ANY($3))
            ORDER BY project.updated_at DESC, project.project_id
            LIMIT $4 OFFSET $5
            """);
        command.Parameters.AddWithValue(includeAll);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            siteIds?.Select(static value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal).ToArray()
            ?? []);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(offset);
        var values = new List<ResearchProject>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<ResearchProject>(reader.GetString(0)));
        return values;
    }

    public async Task<ResearchProject> SaveProjectAsync(
        ResearchProject value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO process_research_projects
              (project_id, code, status, revision, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (project_id) DO UPDATE SET
              status = EXCLUDED.status,
              revision = EXCLUDED.revision,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            WHERE process_research_projects.revision = EXCLUDED.revision - 1
            """;
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Code);
        command.Parameters.AddWithValue(value.Status);
        command.Parameters.AddWithValue(value.Revision);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        command.Parameters.AddWithValue(value.UpdatedAt);
        try
        {
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new ProcessResearchRuleException(
                    "研发项目已被其他人修改，请刷新后重试。");
            await using var deleteMembers = connection.CreateCommand();
            deleteMembers.Transaction = transaction;
            deleteMembers.CommandText =
                "DELETE FROM research_project_members WHERE project_id = $1";
            deleteMembers.Parameters.AddWithValue(value.ProjectId);
            await deleteMembers.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            foreach (var member in value.MemberUserIds
                         .Append(value.OwnerUserId)
                         .Distinct(StringComparer.Ordinal))
            {
                await using var addMember = connection.CreateCommand();
                addMember.Transaction = transaction;
                addMember.CommandText =
                    """
                    INSERT INTO research_project_members(project_id, user_id)
                    VALUES ($1, $2)
                    ON CONFLICT DO NOTHING
                    """;
                addMember.Parameters.AddWithValue(value.ProjectId);
                addMember.Parameters.AddWithValue(member);
                await addMember.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return value;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ProcessResearchRuleException("研发项目代码已经存在。");
        }
    }

}
