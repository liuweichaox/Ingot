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
                INSERT INTO research_experiment_runs(experiment_id, execution_key, sequence, payload)
                VALUES ($1, $2, $3, $4)
                """;
            addRun.Parameters.AddWithValue(value.ExperimentId);
            addRun.Parameters.AddWithValue(run.ExecutionKey);
            addRun.Parameters.AddWithValue(run.Sequence);
            AddJson(addRun, run);
            await addRun.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE research_experiments
                SET status = $3, payload = $2, updated_at = $4
                WHERE experiment_id = $1
                  AND status = 'planned'
                  AND (
                    payload -> 'controlledDecision' IS NULL OR
                    payload -> 'controlledDecision' = 'null'::jsonb
                  )
                """;
            update.Parameters.AddWithValue(updatedExperiment.ExperimentId);
            AddJson(update, updatedExperiment);
            update.Parameters.AddWithValue(updatedExperiment.Status);
            update.Parameters.AddWithValue(updatedExperiment.UpdatedAt);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new ProcessResearchRuleException(
                    "受控在线建议不存在、状态已变化，或人工决策已经冻结，不能覆盖。");
        }

        await using (var deleteRuns = connection.CreateCommand())
        {
            deleteRuns.Transaction = transaction;
            deleteRuns.CommandText = "DELETE FROM research_experiment_runs WHERE experiment_id = $1";
            deleteRuns.Parameters.AddWithValue(updatedExperiment.ExperimentId);
            await deleteRuns.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var run in updatedExperiment.RunPlan)
        {
            await using var addRun = connection.CreateCommand();
            addRun.Transaction = transaction;
            addRun.CommandText =
                """
                INSERT INTO research_experiment_runs(experiment_id, execution_key, sequence, payload)
                VALUES ($1, $2, $3, $4)
                """;
            addRun.Parameters.AddWithValue(updatedExperiment.ExperimentId);
            addRun.Parameters.AddWithValue(run.ExecutionKey);
            addRun.Parameters.AddWithValue(run.Sequence);
            AddJson(addRun, run);
            await addRun.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
        return updatedExperiment;
    }

    public Task<ResearchShadowRecommendation?> GetShadowRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchShadowRecommendation>(
            "SELECT payload FROM research_shadow_recommendations WHERE recommendation_id = $1",
            recommendationId,
            ct);

    public async Task<ResearchShadowRecommendation?> GetShadowRecommendationBySuggestionAsync(
        Guid experimentId,
        string suggestionExecutionKey,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT payload
            FROM research_shadow_recommendations
            WHERE experiment_id = $1 AND suggestion_execution_key = $2
            """;
        command.Parameters.AddWithValue(experimentId);
        command.Parameters.AddWithValue(suggestionExecutionKey);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? null
            : JsonSerializer.Deserialize<ResearchShadowRecommendation>(
                payload.ToString()!, JsonOptions);
    }

    public Task<IReadOnlyList<ResearchShadowRecommendation>> ListShadowRecommendationsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchShadowRecommendation>(
            """
            SELECT payload
            FROM research_shadow_recommendations
            WHERE project_id = $1
            ORDER BY decided_at DESC, recommendation_id
            """,
            projectId,
            ct);

    public async Task<ResearchShadowRecommendation> CreateShadowRecommendationAsync(
        ResearchShadowRecommendation value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO research_shadow_recommendations
              (recommendation_id, project_id, experiment_id, suggestion_execution_key,
               actual_execution_key, decision, payload, decided_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """;
        command.Parameters.AddWithValue(value.RecommendationId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.ExperimentId);
        command.Parameters.AddWithValue(value.SuggestionExecutionKey);
        command.Parameters.AddWithValue(value.ActualExecutionKey);
        command.Parameters.AddWithValue(value.Decision);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.DecidedAt);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return value;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ProcessResearchRuleException("该模型建议或实际运行已经登记影子决策。");
        }
    }

    public async Task<ResearchShadowRecommendation> AttachShadowOutcomeAsync(
        ResearchShadowRecommendation value,
        CancellationToken ct = default)
    {
        if (value.Outcome is null)
            throw new ArgumentException("影子结果不能为空。", nameof(value));
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE research_shadow_recommendations
            SET payload = $2
            WHERE recommendation_id = $1
              AND (
                payload -> 'outcome' IS NULL OR
                payload -> 'outcome' = 'null'::jsonb
              )
            """;
        command.Parameters.AddWithValue(value.RecommendationId);
        AddJson(command, value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("影子建议不存在，或结果已经冻结。不能覆盖源数据结果。");
        return value;
    }

    public Task<ResearchHistoricalReplayReport?> GetHistoricalReplayReportAsync(
        Guid reportId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchHistoricalReplayReport>(
            "SELECT payload FROM research_historical_replay_reports WHERE report_id = $1",
            reportId,
            ct);

    public Task<IReadOnlyList<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchHistoricalReplayReport>(
            """
            SELECT payload
            FROM research_historical_replay_reports
            WHERE project_id = $1
            ORDER BY generated_at DESC, report_id
            """,
            projectId,
            ct);

    public async Task<ResearchHistoricalReplayReport> CreateHistoricalReplayReportAsync(
        ResearchHistoricalReplayReport value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO research_historical_replay_reports
              (report_id, project_id, status, dataset_snapshot_hash, report_hash,
               payload, generated_at, reviewed_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, NULL)
            """;
        command.Parameters.AddWithValue(value.ReportId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Status);
        command.Parameters.AddWithValue(value.DatasetSnapshotHash);
        command.Parameters.AddWithValue(value.ReportHash);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.GeneratedAt);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return value;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var existing = (await ListHistoricalReplayReportsAsync(value.ProjectId, ct)
                    .ConfigureAwait(false))
                .FirstOrDefault(item => item.DatasetSnapshotHash == value.DatasetSnapshotHash &&
                    item.ReportHash == value.ReportHash);
            return existing ?? throw new ProcessResearchRuleException("历史回放报告已经存在。");
        }
    }

    public async Task<ResearchHistoricalReplayReport> ReviewHistoricalReplayReportAsync(
        ResearchHistoricalReplayReport value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE research_historical_replay_reports
            SET status = $2, payload = $3, reviewed_at = $4
            WHERE report_id = $1 AND status = 'generated'
            """;
        command.Parameters.AddWithValue(value.ReportId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.ReviewedAt!.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("历史回放报告不存在或已经审核，不能覆盖。 ");
        return value;
    }

    public Task<ResearchRollbackDrill?> GetRollbackDrillAsync(
        Guid drillId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRollbackDrill>(
            "SELECT payload FROM research_rollback_drills WHERE drill_id = $1",
            drillId,
            ct);

    public Task<IReadOnlyList<ResearchRollbackDrill>> ListRollbackDrillsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchRollbackDrill>(
            """
            SELECT payload
            FROM research_rollback_drills
            WHERE project_id = $1
            ORDER BY recorded_at DESC, drill_id
            """,
            projectId,
            ct);

    public async Task<ResearchRollbackDrill> CreateRollbackDrillAsync(
        ResearchRollbackDrill value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO research_rollback_drills
              (drill_id, project_id, status, passed, record_hash, payload,
               conducted_at, recorded_at, reviewed_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NULL)
            """);
        command.Parameters.AddWithValue(value.DrillId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Status);
        command.Parameters.AddWithValue(value.Passed);
        command.Parameters.AddWithValue(value.RecordHash);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.ConductedAt);
        command.Parameters.AddWithValue(value.RecordedAt);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return value;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ProcessResearchRuleException("停止与回退演练记录已经存在。");
        }
    }

    public async Task<ResearchRollbackDrill> ReviewRollbackDrillAsync(
        ResearchRollbackDrill value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE research_rollback_drills
            SET status = $2, payload = $3, reviewed_at = $4
            WHERE drill_id = $1 AND status = 'recorded'
            """);
        command.Parameters.AddWithValue(value.DrillId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.ReviewedAt!.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("回退演练不存在或已经复核，不能覆盖。");
        return value;
    }

    public Task<ResearchTransferAssessment?> GetTransferAssessmentAsync(
        Guid assessmentId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchTransferAssessment>(
            "SELECT payload FROM research_transfer_assessments WHERE assessment_id = $1",
            assessmentId,
            ct);

    public Task<IReadOnlyList<ResearchTransferAssessment>> ListTransferAssessmentsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchTransferAssessment>(
            """
            SELECT payload
            FROM research_transfer_assessments
            WHERE project_id = $1
            ORDER BY created_at DESC, assessment_id
            """,
            projectId,
            ct);

    public async Task<ResearchTransferAssessment> CreateTransferAssessmentAsync(
        ResearchTransferAssessment value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO research_transfer_assessments
              (assessment_id, project_id, source_project_id, source_operating_region_id,
               status, outcome, record_hash, payload, created_at, reviewed_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, NULL)
            ON CONFLICT (project_id, source_operating_region_id, record_hash) DO NOTHING
            """);
        command.Parameters.AddWithValue(value.AssessmentId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.SourceProjectId);
        command.Parameters.AddWithValue(value.SourceOperatingRegionId);
        command.Parameters.AddWithValue(value.Status);
        command.Parameters.AddWithValue(value.Outcome);
        command.Parameters.AddWithValue(value.RecordHash);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
            return value;
        return (await ListTransferAssessmentsAsync(value.ProjectId, ct).ConfigureAwait(false))
               .First(item => item.SourceOperatingRegionId == value.SourceOperatingRegionId &&
                              item.RecordHash == value.RecordHash);
    }

    public async Task<ResearchTransferAssessment> ReviewTransferAssessmentAsync(
        ResearchTransferAssessment value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE research_transfer_assessments
            SET status = $2, payload = $3, reviewed_at = $4
            WHERE assessment_id = $1 AND status = 'recorded'
            """);
        command.Parameters.AddWithValue(value.AssessmentId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.ReviewedAt!.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("迁移评估不存在或已经复核，不能覆盖。");
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

    public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
        Guid operatingRegionId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchOperatingRegion>(
            "SELECT payload FROM research_operating_regions WHERE operating_region_id = $1",
            operatingRegionId,
            ct);

    public Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchOperatingRegion>(
            """
            SELECT payload
            FROM research_operating_regions
            WHERE project_id = $1
            ORDER BY updated_at DESC, operating_region_id
            """,
            projectId,
            ct);

    public async Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
        ResearchOperatingRegion value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO research_operating_regions
              (operating_region_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (operating_region_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """;
        command.Parameters.AddWithValue(value.OperatingRegionId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        command.Parameters.AddWithValue(value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await using var deleteLinks = connection.CreateCommand();
        deleteLinks.Transaction = transaction;
        deleteLinks.CommandText = "DELETE FROM research_operating_region_results WHERE operating_region_id = $1";
        deleteLinks.Parameters.AddWithValue(value.OperatingRegionId);
        await deleteLinks.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var resultId in value.SupportingResultIds)
        {
            await using var addLink = connection.CreateCommand();
            addLink.Transaction = transaction;
            addLink.CommandText =
                """
                INSERT INTO research_operating_region_results(operating_region_id, result_id)
                VALUES ($1, $2)
                """;
            addLink.Parameters.AddWithValue(value.OperatingRegionId);
            addLink.Parameters.AddWithValue(resultId);
            await addLink.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await SyncEvidenceAsync(
            connection,
            transaction,
            "operating-region",
            value.OperatingRegionId.ToString(),
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
