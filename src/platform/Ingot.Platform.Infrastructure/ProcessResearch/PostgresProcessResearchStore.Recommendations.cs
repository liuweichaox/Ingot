// Persists recipe recommendations and their immutable engineering decisions.
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

    public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionAsync(
        Guid decisionId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRecipeRecommendationDecision>(
            """
            SELECT decision.payload
                   || CASE WHEN execution_link.decision_id IS NULL THEN '{}'::jsonb
                           ELSE jsonb_build_object('actualExecutionKey', execution_link.actual_execution_key) END
                   || CASE WHEN outcome.decision_id IS NULL THEN '{}'::jsonb
                           ELSE jsonb_build_object('outcome', outcome.payload) END
            FROM research_recipe_recommendation_decisions AS decision
            LEFT JOIN research_recipe_recommendation_decision_executions AS execution_link
                ON execution_link.decision_id = decision.decision_id
            LEFT JOIN research_recipe_recommendation_decision_outcomes AS outcome
                ON outcome.decision_id = decision.decision_id
            WHERE decision.decision_id = $1
            """,
            decisionId,
            ct);

    public async Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionByItemAsync(
        Guid recommendationId,
        string recommendationKey,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT decision.payload
                   || CASE WHEN execution_link.decision_id IS NULL THEN '{}'::jsonb
                           ELSE jsonb_build_object('actualExecutionKey', execution_link.actual_execution_key) END
                   || CASE WHEN outcome.decision_id IS NULL THEN '{}'::jsonb
                           ELSE jsonb_build_object('outcome', outcome.payload) END
            FROM research_recipe_recommendation_decisions AS decision
            LEFT JOIN research_recipe_recommendation_decision_executions AS execution_link
                ON execution_link.decision_id = decision.decision_id
            LEFT JOIN research_recipe_recommendation_decision_outcomes AS outcome
                ON outcome.decision_id = decision.decision_id
            WHERE decision.recommendation_id = $1 AND decision.recommendation_key = $2
            """);
        command.Parameters.AddWithValue(recommendationId);
        command.Parameters.AddWithValue(recommendationKey);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? null
            : Deserialize<ResearchRecipeRecommendationDecision>((string)payload);
    }

    public Task<ResearchPage<ResearchRecipeRecommendationDecision>> ListRecipeRecommendationDecisionsPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchRecipeRecommendationDecision>(
            """
            SELECT decision.payload
                   || CASE WHEN execution_link.decision_id IS NULL THEN '{}'::jsonb
                           ELSE jsonb_build_object('actualExecutionKey', execution_link.actual_execution_key) END
                   || CASE WHEN outcome.decision_id IS NULL THEN '{}'::jsonb
                           ELSE jsonb_build_object('outcome', outcome.payload) END
            FROM research_recipe_recommendation_decisions AS decision
            LEFT JOIN research_recipe_recommendation_decision_executions AS execution_link
                ON execution_link.decision_id = decision.decision_id
            LEFT JOIN research_recipe_recommendation_decision_outcomes AS outcome
                ON outcome.decision_id = decision.decision_id
            WHERE decision.project_id = $1
              AND ($2::timestamptz IS NULL OR (decision.decided_at, decision.decision_id) < ($2, $3))
            ORDER BY decision.decided_at DESC, decision.decision_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.DecidedAt,
            static value => value.DecisionId,
            ct);

    public async Task<ResearchRecipeRecommendationDecision> CreateRecipeRecommendationDecisionTransactionAsync(
        ResearchRecipeRecommendationDecision value,
        string? actualExecutionKey,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO research_recipe_recommendation_decisions
                      (decision_id, project_id, recommendation_id, recommendation_key, decision, payload, decided_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7)
                    """;
                command.Parameters.AddWithValue(value.DecisionId);
                command.Parameters.AddWithValue(value.ProjectId);
                command.Parameters.AddWithValue(value.RecommendationId);
                command.Parameters.AddWithValue(value.RecommendationKey);
                command.Parameters.AddWithValue(value.Decision);
                AddJson(command, value);
                command.Parameters.AddWithValue(value.DecidedAt);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(actualExecutionKey))
                await InsertRecipeRecommendationDecisionExecutionLinkAsync(
                    connection, transaction, value.DecisionId, actualExecutionKey, ct).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw new ProcessResearchRuleException("该配方建议项或实际运行已经登记工程师决策。");
        }
        return (await GetRecipeRecommendationDecisionAsync(value.DecisionId, ct).ConfigureAwait(false))
            ?? throw new ProcessResearchRuleException("日常建议决策不存在。不能读取已冻结的决定。");
    }

    public async Task<ResearchRecipeRecommendationDecision> LinkRecipeRecommendationDecisionExecutionTransactionAsync(
        Guid decisionId,
        string actualExecutionKey,
        ResearchAuditEntry audit,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await InsertRecipeRecommendationDecisionExecutionLinkAsync(
                connection, transaction, decisionId, actualExecutionKey, ct).ConfigureAwait(false);
            await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            var existing = await GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false);
            if (existing?.ActualExecutionKey == actualExecutionKey)
                return existing;
            throw new ProcessResearchRuleException("该工程师决定或实际运行已经关联，不能覆盖。");
        }
        return (await GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false))
            ?? throw new ProcessResearchRuleException("日常建议决策不存在。不能读取已关联的实际运行。");
    }

    public async Task<ResearchRecipeRecommendationDecision> AttachRecipeRecommendationOutcomeTransactionAsync(
        Guid decisionId,
        ResearchRecipeRecommendationOutcome outcome,
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
                INSERT INTO research_recipe_recommendation_decision_outcomes
                  (decision_id, project_id, payload, materialized_at)
                SELECT decision.decision_id, decision.project_id, $2::jsonb, $3
                FROM research_recipe_recommendation_decisions AS decision
                JOIN research_recipe_recommendation_decision_executions AS execution_link
                    ON execution_link.decision_id = decision.decision_id
                WHERE decision.decision_id = $1
                  AND execution_link.actual_execution_key = $4
                ON CONFLICT (decision_id) DO NOTHING
                """;
            command.Parameters.AddWithValue(decisionId);
            AddJson(command, outcome);
            command.Parameters.AddWithValue(outcome.CapturedAt);
            command.Parameters.AddWithValue(outcome.ActualExecutionKey);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                var existing = await GetRecipeRecommendationDecisionAsync(decisionId, ct)
                    .ConfigureAwait(false);
                if (existing?.Outcome is not null)
                    return existing;
                throw new ProcessResearchRuleException("日常建议决策不存在。不能冻结源数据结果。");
            }
        }
        await InsertAuditAsync(connection, transaction, audit, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return (await GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false))
            ?? throw new ProcessResearchRuleException("日常建议决策不存在。不能读取已冻结的源数据结果。");
    }

    private static async Task InsertRecipeRecommendationDecisionExecutionLinkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid decisionId,
        string actualExecutionKey,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO research_recipe_recommendation_decision_executions
              (decision_id, project_id, actual_execution_key, linked_at)
            SELECT decision_id, project_id, $2, now()
            FROM research_recipe_recommendation_decisions
            WHERE decision_id = $1
            """;
        command.Parameters.AddWithValue(decisionId);
        command.Parameters.AddWithValue(actualExecutionKey);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("日常建议决策不存在。不能关联实际运行。");
    }

}
