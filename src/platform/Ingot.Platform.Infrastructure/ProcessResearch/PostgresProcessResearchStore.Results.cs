// 持久化实验结果、运行区域和知识声明。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
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

    public Task<ResearchPage<ResearchExperimentResult>> ListExperimentResultsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchExperimentResult>(
            """
            SELECT payload
            FROM research_experiment_results
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (recorded_at, result_id) < ($2, $3))
            ORDER BY recorded_at DESC, result_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.RecordedAt,
            static value => value.ResultId,
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
                    SET status = $2,
                        revision = $3,
                        payload = $4,
                        updated_at = $5
                    WHERE experiment_id = $1
                      AND revision = $3 - 1
                    """;
                updateExperiment.Parameters.AddWithValue(updatedExperiment.ExperimentId);
                updateExperiment.Parameters.AddWithValue(updatedExperiment.Status);
                updateExperiment.Parameters.AddWithValue(updatedExperiment.Revision);
                AddJson(updateExperiment, updatedExperiment);
                updateExperiment.Parameters.AddWithValue(updatedExperiment.UpdatedAt);
                if (await updateExperiment.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                    throw new ProcessResearchRuleException(
                        "实验已被其他人修改，请刷新后重试。");
            }

            await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
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

}
