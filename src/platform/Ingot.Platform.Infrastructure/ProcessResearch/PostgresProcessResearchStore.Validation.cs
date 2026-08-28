// 持久化影子验证、历史回放、回退演练和迁移评估。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
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

    public Task<ResearchPage<ResearchShadowRecommendation>> ListShadowRecommendationsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchShadowRecommendation>(
            """
            SELECT payload
            FROM research_shadow_recommendations
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (decided_at, recommendation_id) < ($2, $3))
            ORDER BY decided_at DESC, recommendation_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.DecidedAt,
            static value => value.RecommendationId,
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

    public Task<ResearchPage<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchHistoricalReplayReport>(
            """
            SELECT payload
            FROM research_historical_replay_reports
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (generated_at, report_id) < ($2, $3))
            ORDER BY generated_at DESC, report_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.GeneratedAt,
            static value => value.ReportId,
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

}
