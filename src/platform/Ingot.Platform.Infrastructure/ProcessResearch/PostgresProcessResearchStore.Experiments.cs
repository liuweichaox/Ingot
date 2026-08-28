// 持久化配方建议和研究实验。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
    public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRecipeRecommendation>(
            "SELECT payload FROM research_recipe_recommendations WHERE recommendation_id = $1",
            recommendationId,
            ct);

    public async Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
        Guid projectId,
        string inputHash,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT payload
            FROM research_recipe_recommendations
            WHERE project_id = $1 AND input_hash = $2
            """);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(inputHash);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? null
            : Deserialize<ResearchRecipeRecommendation>((string)payload);
    }

    public Task<IReadOnlyList<ResearchRecipeRecommendation>> ListRecipeRecommendationsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchRecipeRecommendation>(
            """
            SELECT payload
            FROM research_recipe_recommendations
            WHERE project_id = $1
            ORDER BY generated_at DESC, recommendation_id DESC
            """,
            projectId,
            ct);

    public Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchRecipeRecommendation>(
            """
            SELECT payload
            FROM research_recipe_recommendations
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (generated_at, recommendation_id) < ($2, $3))
            ORDER BY generated_at DESC, recommendation_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.GeneratedAt,
            static value => value.RecommendationId,
            ct);

    public async Task<ResearchRecipeRecommendation> CreateRecipeRecommendationTransactionAsync(
        ResearchRecipeRecommendation value,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO research_recipe_recommendations
                  (recommendation_id, project_id, input_hash, payload, generated_at)
                VALUES ($1, $2, $3, $4, $5)
                ON CONFLICT DO NOTHING
                """;
            command.Parameters.AddWithValue(value.RecommendationId);
            command.Parameters.AddWithValue(value.ProjectId);
            command.Parameters.AddWithValue(value.InputHash);
            AddJson(command, value);
            command.Parameters.AddWithValue(value.GeneratedAt);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new ProcessResearchRuleException("相同输入快照的配方建议已经生成，请刷新后重试。");
        }
        await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
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

    public Task<ResearchPage<ResearchExperiment>> ListExperimentsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchExperiment>(
            """
            SELECT payload
            FROM research_experiments
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (updated_at, experiment_id) < ($2, $3))
            ORDER BY updated_at DESC, experiment_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.UpdatedAt,
            static value => value.ExperimentId,
            ct);

    public async Task<ResearchExperiment> SaveExperimentAsync(
        ResearchExperiment value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveExperimentCoreAsync(connection, transaction, value, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<ResearchExperiment> SaveExperimentTransactionAsync(
        ResearchExperiment updatedExperiment,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveExperimentCoreAsync(connection, transaction, updatedExperiment, ct).ConfigureAwait(false);
        await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return updatedExperiment;
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
                SET status = $3, revision = $5, payload = $2, updated_at = $4
                WHERE experiment_id = $1
                  AND revision = $5 - 1
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
            update.Parameters.AddWithValue(updatedExperiment.Revision);
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
        await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return updatedExperiment;
    }

}
