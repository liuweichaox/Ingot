// 持久化下一配方建议及其不可变的工程师决定、实际运行关联和结果证据。
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
    private const string DecisionSelect =
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
        """;

    public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRecipeRecommendation>(
            "SELECT payload FROM research_recipe_recommendations WHERE recommendation_id = @id",
            command => command.Parameters.AddWithValue("id", recommendationId),
            ct);

    public Task<ResearchRecipeRecommendation?> GetRecipeRecommendationByInputHashAsync(
        string siteCode,
        string processSpecificationId,
        string inputHash,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRecipeRecommendation>(
            """
            SELECT payload
            FROM research_recipe_recommendations
            WHERE site_code = @site_code
              AND process_specification_id = @process_specification_id
              AND input_hash = @input_hash
            """,
            command =>
            {
                command.Parameters.AddWithValue("site_code", siteCode);
                command.Parameters.AddWithValue("process_specification_id", processSpecificationId);
                command.Parameters.AddWithValue("input_hash", inputHash);
            },
            ct);

    public Task<ResearchPage<ResearchRecipeRecommendation>> ListRecipeRecommendationsPageAsync(
        RecipeRecommendationFilter filter,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchRecipeRecommendation>(
            """
            SELECT payload
            FROM research_recipe_recommendations
            WHERE (@all_sites OR site_code = ANY(@site_codes))
              AND (@process_specification_id::text IS NULL
                   OR process_specification_id = @process_specification_id)
              AND (@before_at IS NULL OR (generated_at, recommendation_id) < (@before_at, @before_id))
            ORDER BY generated_at DESC, recommendation_id DESC
            LIMIT @take
            """,
            command =>
            {
                command.Parameters.AddWithValue("all_sites", filter.SiteCodes is null);
                command.Parameters.AddWithValue(
                    "site_codes",
                    NpgsqlDbType.Array | NpgsqlDbType.Text,
                    filter.SiteCodes?.Distinct(StringComparer.Ordinal).ToArray() ?? []);
                AddNullable(command, "process_specification_id", NpgsqlDbType.Text,
                    string.IsNullOrWhiteSpace(filter.ProcessSpecificationId)
                        ? null
                        : filter.ProcessSpecificationId.Trim());
            },
            cursor,
            limit,
            static value => value.GeneratedAt,
            static value => value.RecommendationId,
            ct);

    public async Task<ResearchRecipeRecommendation> CreateRecipeRecommendationAsync(
        ResearchRecipeRecommendation value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO research_recipe_recommendations
              (recommendation_id, site_code, process_specification_id, input_hash, payload, generated_at)
            VALUES (@id, @site_code, @process_specification_id, @input_hash, @payload, @generated_at)
            ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue("id", value.RecommendationId);
        command.Parameters.AddWithValue("site_code", value.SiteCode);
        command.Parameters.AddWithValue("process_specification_id", value.ProcessSpecificationId);
        command.Parameters.AddWithValue("input_hash", value.InputHash);
        AddJson(command, "payload", value);
        command.Parameters.AddWithValue("generated_at", value.GeneratedAt);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("相同输入快照的配方建议已经生成，请刷新后重试。");
        return value;
    }

    public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionAsync(
        Guid decisionId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRecipeRecommendationDecision>(
            DecisionSelect + " WHERE decision.decision_id = @id",
            command => command.Parameters.AddWithValue("id", decisionId),
            ct);

    public Task<ResearchRecipeRecommendationDecision?> GetRecipeRecommendationDecisionByItemAsync(
        Guid recommendationId,
        string recommendationKey,
        CancellationToken ct = default)
        => GetOneAsync<ResearchRecipeRecommendationDecision>(
            DecisionSelect +
            " WHERE decision.recommendation_id = @recommendation_id AND decision.recommendation_key = @key",
            command =>
            {
                command.Parameters.AddWithValue("recommendation_id", recommendationId);
                command.Parameters.AddWithValue("key", recommendationKey);
            },
            ct);

    public Task<IReadOnlyList<ResearchRecipeRecommendationDecision>> ListPendingRecipeRecommendationDecisionsAsync(
        string siteCode,
        string processSpecificationId,
        CancellationToken ct = default)
        => ListAsync<ResearchRecipeRecommendationDecision>(
            DecisionSelect +
            """

            WHERE decision.site_code = @site_code
              AND decision.process_specification_id = @process_specification_id
              AND decision.decision IN ('accepted', 'modified')
              AND outcome.decision_id IS NULL
            ORDER BY decision.decided_at, decision.decision_id
            """,
            command =>
            {
                command.Parameters.AddWithValue("site_code", siteCode);
                command.Parameters.AddWithValue("process_specification_id", processSpecificationId);
            },
            ct);

    public async Task<ResearchRecipeRecommendationDecision> CreateRecipeRecommendationDecisionTransactionAsync(
        ResearchRecipeRecommendationDecision value,
        string? actualExecutionKey,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var command = new NpgsqlCommand(
                """
                INSERT INTO research_recipe_recommendation_decisions
                  (decision_id, site_code, process_specification_id, recommendation_id,
                   recommendation_key, decision, payload, decided_at)
                VALUES (@id, @site_code, @process_specification_id, @recommendation_id,
                   @key, @decision, @payload, @decided_at)
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("id", value.DecisionId);
                command.Parameters.AddWithValue("site_code", value.SiteCode);
                command.Parameters.AddWithValue("process_specification_id", value.ProcessSpecificationId);
                command.Parameters.AddWithValue("recommendation_id", value.RecommendationId);
                command.Parameters.AddWithValue("key", value.RecommendationKey);
                command.Parameters.AddWithValue("decision", value.Decision);
                AddJson(command, "payload", value with { ActualExecutionKey = null, Outcome = null });
                command.Parameters.AddWithValue("decided_at", value.DecidedAt);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(actualExecutionKey))
                await InsertExecutionLinkAsync(
                    connection, transaction, value.DecisionId, actualExecutionKey, value.DecidedBy, ct)
                    .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState is
            PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
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
        string linkedBy,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await InsertExecutionLinkAsync(connection, transaction, decisionId, actualExecutionKey, linkedBy, ct)
                .ConfigureAwait(false);
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
        string materializedBy,
        CancellationToken ct = default)
    {
        await using (var command = _dataSource.CreateCommand(
            """
            INSERT INTO research_recipe_recommendation_decision_outcomes
              (decision_id, payload, materialized_by, materialized_at)
            SELECT execution_link.decision_id, @payload, @materialized_by, @materialized_at
            FROM research_recipe_recommendation_decision_executions AS execution_link
            WHERE execution_link.decision_id = @id
              AND execution_link.actual_execution_key = @execution_key
            ON CONFLICT (decision_id) DO NOTHING
            """))
        {
            command.Parameters.AddWithValue("id", decisionId);
            AddJson(command, "payload", outcome);
            command.Parameters.AddWithValue("materialized_by", materializedBy);
            command.Parameters.AddWithValue("materialized_at", outcome.CapturedAt);
            command.Parameters.AddWithValue("execution_key", outcome.ActualExecutionKey);
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            {
                var existing = await GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false);
                if (existing?.Outcome is not null)
                    return existing;
                throw new ProcessResearchRuleException("日常建议决策尚未关联该实际运行，不能冻结源数据结果。");
            }
        }
        return (await GetRecipeRecommendationDecisionAsync(decisionId, ct).ConfigureAwait(false))
            ?? throw new ProcessResearchRuleException("日常建议决策不存在。不能读取已冻结的源数据结果。");
    }

    private static async Task InsertExecutionLinkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid decisionId,
        string actualExecutionKey,
        string linkedBy,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO research_recipe_recommendation_decision_executions
              (decision_id, actual_execution_key, linked_by, linked_at)
            SELECT decision_id, @execution_key, @linked_by, now()
            FROM research_recipe_recommendation_decisions
            WHERE decision_id = @id AND decision IN ('accepted', 'modified')
            """, connection, transaction);
        command.Parameters.AddWithValue("id", decisionId);
        command.Parameters.AddWithValue("execution_key", actualExecutionKey);
        command.Parameters.AddWithValue("linked_by", linkedBy);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("日常建议决策不存在或已拒绝，不能关联实际运行。");
    }
}
