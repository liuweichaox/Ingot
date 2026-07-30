using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ingot.Contracts.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed class PostgresProcessResearchStore : IProcessResearchStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessResearchStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException(
                "缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

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
            ORDER BY project.updated_at DESC, project.project_id
            LIMIT $3 OFFSET $4
            """);
        command.Parameters.AddWithValue(includeAll);
        command.Parameters.AddWithValue(userId);
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

    public Task<ResearchHypothesis?> GetHypothesisAsync(
        Guid hypothesisId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchHypothesis>(
            "SELECT payload FROM research_hypotheses WHERE hypothesis_id = $1",
            hypothesisId,
            ct);

    public Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchHypothesis>(
            """
            SELECT payload
            FROM research_hypotheses
            WHERE project_id = $1
            ORDER BY updated_at DESC, hypothesis_id
            """,
            projectId,
            ct);

    public async Task<ResearchHypothesis> SaveHypothesisAsync(
        ResearchHypothesis value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveChildAsync(
            connection,
            transaction,
            """
            INSERT INTO research_hypotheses
              (hypothesis_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (hypothesis_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """,
            value.HypothesisId,
            value.ProjectId,
            value.Status,
            value,
            value.CreatedAt,
            value.UpdatedAt,
            ct).ConfigureAwait(false);
        await SyncEvidenceAsync(
            connection,
            transaction,
            "hypothesis",
            value.HypothesisId.ToString(),
            value.SupportingEvidence.Concat(value.OpposingEvidence).ToArray(),
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ResearchExperiment?> GetExperimentAsync(
        Guid experimentId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchExperiment>(
            "SELECT payload FROM research_experiments WHERE experiment_id = $1",
            experimentId,
            ct);

    public Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchExperiment>(
            """
            SELECT payload
            FROM research_experiments
            WHERE project_id = $1
            ORDER BY updated_at DESC, experiment_id
            """,
            projectId,
            ct);

    public async Task<ResearchExperiment> SaveExperimentAsync(
        ResearchExperiment value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO research_experiments
              (experiment_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (experiment_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """;
        command.Parameters.AddWithValue(value.ExperimentId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        command.Parameters.AddWithValue(value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using var deleteRuns = connection.CreateCommand();
        deleteRuns.Transaction = transaction;
        deleteRuns.CommandText = "DELETE FROM research_experiment_runs WHERE experiment_id = $1";
        deleteRuns.Parameters.AddWithValue(value.ExperimentId);
        await deleteRuns.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var run in value.RunPlan)
        {
            await using var addRun = connection.CreateCommand();
            addRun.Transaction = transaction;
            addRun.CommandText =
                """
                INSERT INTO research_experiment_runs(experiment_id, run_key, sequence, payload)
                VALUES ($1, $2, $3, $4)
                """;
            addRun.Parameters.AddWithValue(value.ExperimentId);
            addRun.Parameters.AddWithValue(run.RunKey);
            addRun.Parameters.AddWithValue(run.Sequence);
            AddJson(addRun, run);
            await addRun.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ResearchExperimentResult?> GetExperimentResultAsync(
        Guid resultId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchExperimentResult>(
            "SELECT payload FROM research_experiment_results WHERE result_id = $1",
            resultId,
            ct);

    public Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchExperimentResult>(
            """
            SELECT payload
            FROM research_experiment_results
            WHERE project_id = $1
            ORDER BY recorded_at DESC, result_id
            """,
            projectId,
            ct);

    public async Task<ResearchExperimentResult> SaveExperimentResultAsync(
        ResearchExperimentResult value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO research_experiment_results
              (result_id, project_id, experiment_id, analysis_run_id, analysis_hash,
               safety_passed, payload, recorded_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """;
        command.Parameters.AddWithValue(value.ResultId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.ExperimentId);
        command.Parameters.AddWithValue(value.AnalysisRunId);
        command.Parameters.AddWithValue(value.AnalysisHash);
        command.Parameters.AddWithValue(value.SafetyPassed);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.RecordedAt);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await SyncEvidenceAsync(
                connection,
                transaction,
                "experiment-result",
                value.ResultId.ToString(),
                value.Evidence,
                ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return value;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ProcessResearchRuleException("实验结果已经存在。");
        }
    }

    public async Task<ResearchExperimentResult> SaveExperimentResultTransactionAsync(
        ResearchExperimentResult result,
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var insertResult = connection.CreateCommand())
            {
                insertResult.Transaction = transaction;
                insertResult.CommandText =
                    """
                    INSERT INTO research_experiment_results
                      (result_id, project_id, experiment_id, analysis_run_id, analysis_hash,
                       safety_passed, payload, recorded_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
                    """;
                insertResult.Parameters.AddWithValue(result.ResultId);
                insertResult.Parameters.AddWithValue(result.ProjectId);
                insertResult.Parameters.AddWithValue(result.ExperimentId);
                insertResult.Parameters.AddWithValue(result.AnalysisRunId);
                insertResult.Parameters.AddWithValue(result.AnalysisHash);
                insertResult.Parameters.AddWithValue(result.SafetyPassed);
                AddJson(insertResult, result);
                insertResult.Parameters.AddWithValue(result.RecordedAt);
                await insertResult.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await SyncEvidenceAsync(
                connection,
                transaction,
                "experiment-result",
                result.ResultId.ToString(),
                result.Evidence,
                ct).ConfigureAwait(false);

            await using (var updateExperiment = connection.CreateCommand())
            {
                updateExperiment.Transaction = transaction;
                updateExperiment.CommandText =
                    """
                    UPDATE research_experiments
                    SET payload = jsonb_set(
                          jsonb_set(
                            payload,
                            '{resultIds}',
                            coalesce(payload -> 'resultIds', '[]'::jsonb)
                              || to_jsonb(ARRAY[$2::text]),
                            true),
                          '{updatedAt}',
                          to_jsonb($4::text),
                          true),
                        updated_at = $3
                    WHERE experiment_id = $1
                    """;
                updateExperiment.Parameters.AddWithValue(updatedExperiment.ExperimentId);
                updateExperiment.Parameters.AddWithValue(result.ResultId);
                updateExperiment.Parameters.AddWithValue(updatedExperiment.UpdatedAt);
                updateExperiment.Parameters.AddWithValue(
                    updatedExperiment.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
                if (await updateExperiment.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new ProcessResearchRuleException("实验不存在。");
            }

            await using (var insertAudit = connection.CreateCommand())
            {
                insertAudit.Transaction = transaction;
                insertAudit.CommandText =
                    """
                    INSERT INTO process_research_audit
                      (entry_id, project_id, resource_type, resource_id, action, payload, created_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7)
                    """;
                insertAudit.Parameters.AddWithValue(audit.EntryId);
                insertAudit.Parameters.AddWithValue(audit.ProjectId);
                insertAudit.Parameters.AddWithValue(audit.ResourceType);
                insertAudit.Parameters.AddWithValue(audit.ResourceId);
                insertAudit.Parameters.AddWithValue(audit.Action);
                AddJson(insertAudit, audit);
                insertAudit.Parameters.AddWithValue(audit.CreatedAt);
                await insertAudit.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ProcessResearchRuleException("实验结果已经存在。");
        }
    }

    public Task<ResearchProcessWindow?> GetProcessWindowAsync(
        Guid windowId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchProcessWindow>(
            "SELECT payload FROM research_process_windows WHERE window_id = $1",
            windowId,
            ct);

    public Task<IReadOnlyList<ResearchProcessWindow>> ListProcessWindowsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchProcessWindow>(
            """
            SELECT payload
            FROM research_process_windows
            WHERE project_id = $1
            ORDER BY updated_at DESC, window_id
            """,
            projectId,
            ct);

    public async Task<ResearchProcessWindow> SaveProcessWindowAsync(
        ResearchProcessWindow value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO research_process_windows
              (window_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (window_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """;
        command.Parameters.AddWithValue(value.WindowId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        command.Parameters.AddWithValue(value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await using var deleteLinks = connection.CreateCommand();
        deleteLinks.Transaction = transaction;
        deleteLinks.CommandText = "DELETE FROM research_window_results WHERE window_id = $1";
        deleteLinks.Parameters.AddWithValue(value.WindowId);
        await deleteLinks.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var resultId in value.SupportingResultIds)
        {
            await using var addLink = connection.CreateCommand();
            addLink.Transaction = transaction;
            addLink.CommandText =
                """
                INSERT INTO research_window_results(window_id, result_id)
                VALUES ($1, $2)
                """;
            addLink.Parameters.AddWithValue(value.WindowId);
            addLink.Parameters.AddWithValue(resultId);
            await addLink.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await SyncEvidenceAsync(
            connection,
            transaction,
            "process-window",
            value.WindowId.ToString(),
            value.Evidence,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(
        Guid claimId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchKnowledgeClaim>(
            "SELECT payload FROM research_knowledge_claims WHERE claim_id = $1",
            claimId,
            ct);

    public Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchKnowledgeClaim>(
            """
            SELECT payload
            FROM research_knowledge_claims
            WHERE project_id = $1
            ORDER BY updated_at DESC, claim_id
            """,
            projectId,
            ct);

    public async Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        ResearchKnowledgeClaim value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveChildAsync(
            connection,
            transaction,
            """
            INSERT INTO research_knowledge_claims
              (claim_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (claim_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """,
            value.ClaimId,
            value.ProjectId,
            value.Status,
            value,
            value.CreatedAt,
            value.UpdatedAt,
            ct).ConfigureAwait(false);
        await SyncEvidenceAsync(
            connection,
            transaction,
            "knowledge-claim",
            value.ClaimId.ToString(),
            value.Evidence,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

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

    private async Task<T?> GetOneAsync<T>(
        string sql,
        object parameter,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(parameter);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? default
            : Deserialize<T>((string)payload);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string sql,
        object? parameter,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        if (parameter is not null)
            command.Parameters.AddWithValue(parameter);
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private static async Task SaveChildAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        Guid id,
        Guid projectId,
        string status,
        T value,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(status);
        AddJson(command, value);
        command.Parameters.AddWithValue(createdAt);
        command.Parameters.AddWithValue(updatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task SyncEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string resourceType,
        string resourceId,
        IReadOnlyList<EvidenceReference> evidence,
        CancellationToken ct)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText =
            "DELETE FROM research_evidence WHERE resource_type = $1 AND resource_id = $2";
        delete.Parameters.AddWithValue(resourceType);
        delete.Parameters.AddWithValue(resourceId);
        await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var item in evidence
                     .GroupBy(static value => (value.Kind, value.ReferenceId))
                     .Select(static group => group.First()))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO research_evidence
                  (evidence_id, project_id, resource_type, resource_id, kind,
                   reference_id, content_hash, payload, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                """;
            insert.Parameters.AddWithValue(item.EvidenceId);
            insert.Parameters.AddWithValue(item.ProjectId);
            insert.Parameters.AddWithValue(resourceType);
            insert.Parameters.AddWithValue(resourceId);
            insert.Parameters.AddWithValue(item.Kind);
            insert.Parameters.AddWithValue(item.ReferenceId);
            insert.Parameters.AddWithValue(item.ContentHash);
            AddJson(insert, item);
            insert.Parameters.AddWithValue(item.CreatedAt);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static void AddJson<T>(NpgsqlCommand command, T value)
        => command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(value, JsonOptions));

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法解析 {typeof(T).Name}。");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new CompatibleDateTimeOffsetConverter());
        return options;
    }

    private sealed class CompatibleDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("DateTimeOffset 必须是字符串。");
            if (reader.TryGetDateTimeOffset(out var parsed))
                return parsed;
            var raw = reader.GetString();
            if (DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return parsed;
            }
            throw new JsonException($"无法解析 DateTimeOffset：{raw}");
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }

    public async ValueTask DisposeAsync()
        => await _dataSource.DisposeAsync().ConfigureAwait(false);
}
