using Ingot.Platform.Application.ResearchAssets;
using Ingot.Contracts.ResearchAssets;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class PostgresMechanismKnowledgeStore : IMechanismKnowledgeStore
{
    private readonly NpgsqlDataSource dataSource;

    public PostgresMechanismKnowledgeStore(NpgsqlDataSource dataSource)
        => this.dataSource = dataSource;

    public async Task<MechanismClaimVersion?> GetClaimAsync(
        Guid claimId,
        int? version = null,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        return await ReadClaimAsync(connection, claimId, version, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MechanismClaimVersion>> ListClaimsAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var keys = new List<(Guid ClaimId, int Version)>();
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
            "SELECT claim_id, current_version FROM mechanism_claims WHERE project_id = @project_id ORDER BY updated_at DESC;",
            connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                keys.Add((reader.GetGuid(0), reader.GetInt32(1)));
        }
        var values = new List<MechanismClaimVersion>(keys.Count);
        foreach (var key in keys)
            if (await ReadClaimAsync(connection, key.ClaimId, key.Version, ct).ConfigureAwait(false) is { } value)
                values.Add(value);
        return values;
    }

    public async Task<MechanismClaimVersion> SaveDraftAsync(
        MechanismClaimVersion value,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var claim = new NpgsqlCommand(
            """
            INSERT INTO mechanism_claims(claim_id, project_id, current_version, status, created_at, updated_at)
            VALUES (@claim_id, @project_id, @version, @status, @created_at, @updated_at)
            ON CONFLICT (claim_id) DO UPDATE SET
              current_version = EXCLUDED.current_version,
              status = EXCLUDED.status,
              updated_at = EXCLUDED.updated_at;
            """, connection, transaction))
        {
            claim.Parameters.AddWithValue("claim_id", value.ClaimId);
            claim.Parameters.AddWithValue("project_id", value.ProjectId);
            claim.Parameters.AddWithValue("version", value.Version);
            claim.Parameters.AddWithValue("status", value.Status);
            claim.Parameters.AddWithValue("created_at", value.CreatedAt);
            claim.Parameters.AddWithValue("updated_at", value.UpdatedAt);
            await claim.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var version = new NpgsqlCommand(
            """
            INSERT INTO mechanism_claim_versions(
              claim_id, version, name, mechanism_type, statement, expected_signature,
              falsification_condition, evidence_level, created_by, created_at, content_hash)
            VALUES (
              @claim_id, @version, @name, @mechanism_type, @statement, @expected_signature,
              @falsification_condition, @evidence_level, @created_by, @created_at, @content_hash);
            """, connection, transaction))
        {
            version.Parameters.AddWithValue("claim_id", value.ClaimId);
            version.Parameters.AddWithValue("version", value.Version);
            version.Parameters.AddWithValue("name", value.Name);
            version.Parameters.AddWithValue("mechanism_type", value.MechanismType);
            version.Parameters.AddWithValue("statement", value.Statement);
            AddNullable(version, "expected_signature", NpgsqlDbType.Text, value.ExpectedSignature);
            version.Parameters.AddWithValue("falsification_condition", value.FalsificationCondition);
            version.Parameters.AddWithValue("evidence_level", value.EvidenceLevel);
            version.Parameters.AddWithValue("created_by", value.CreatedBy);
            version.Parameters.AddWithValue("created_at", value.CreatedAt);
            version.Parameters.AddWithValue("content_hash", value.ContentHash);
            await version.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var item in value.Variables)
            await InsertVariableAsync(connection, transaction, value, item, ct).ConfigureAwait(false);
        foreach (var item in value.Applicability)
            await InsertApplicabilityAsync(connection, transaction, value, item, ct).ConfigureAwait(false);
        foreach (var item in value.Constraints)
            await InsertConstraintAsync(connection, transaction, value, item, ct).ConfigureAwait(false);
        foreach (var item in value.Evidence)
            await InsertEvidenceAsync(connection, transaction, value, item, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<bool> EvidenceExistsAsync(
        Guid projectId,
        MechanismClaimEvidence evidence,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(evidence.ReferenceId, out var referenceId)) return false;
        var sql = evidence.EvidenceKind switch
        {
            "knowledge-source" =>
                "SELECT EXISTS(SELECT 1 FROM knowledge_sources WHERE source_id=@id AND project_id=@project_id AND sha256=@hash);",
            "knowledge-fragment" =>
                """
                SELECT EXISTS(
                  SELECT 1 FROM knowledge_fragments fragment
                  JOIN knowledge_sources source ON source.source_id=fragment.source_id
                  WHERE fragment.record_id=@id AND source.project_id=@project_id AND fragment.content_hash=@hash);
                """,
            "experiment-result" =>
                """
                SELECT EXISTS(
                  SELECT 1 FROM research_experiment_results
                  WHERE result_id=@id AND project_id=@project_id AND analysis_hash=@hash
                    AND safety_passed AND COALESCE((payload->>'calculatedFromSource')::boolean, false));
                """,
            _ => null
        };
        if (sql is null) return false;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", referenceId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("hash", evidence.ContentHash);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
    }

    public async Task<bool> ExperimentResultValidatesClaimAsync(
        Guid projectId,
        MechanismClaimVersion claim,
        Guid validationHypothesisId,
        MechanismClaimEvidence evidence,
        string evaluationOutcome = "supports",
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(evidence.ReferenceId, out var resultId)) return false;
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS(
              SELECT 1
              FROM research_experiment_results result
              JOIN research_experiments experiment ON experiment.experiment_id = result.experiment_id
              JOIN research_hypotheses hypothesis
                ON hypothesis.hypothesis_id = @hypothesis_id
               AND hypothesis.project_id = @project_id
              WHERE result.result_id = @result_id
                AND result.project_id = @project_id
                AND result.analysis_hash = @hash
                AND result.safety_passed
                AND COALESCE((result.payload->>'calculatedFromSource')::boolean, false)
                AND NULLIF(experiment.payload->>'hypothesisId', '')::uuid = hypothesis.hypothesis_id
                AND hypothesis.validation_outcome_code IS NOT NULL
                AND hypothesis.expected_effect_direction IS NOT NULL
                AND hypothesis.minimum_effect IS NOT NULL
                AND EXISTS (
                  SELECT 1 FROM jsonb_array_elements(result.payload->'metrics') metric
                  WHERE metric->>'objectiveCode' = hypothesis.validation_outcome_code
                    AND CASE @evaluation_outcome
                      WHEN 'supports' THEN CASE hypothesis.expected_effect_direction
                        WHEN 'increase' THEN (metric->>'effectValue')::double precision >= hypothesis.minimum_effect
                          AND COALESCE((metric->>'lowerConfidenceBound')::double precision, '-Infinity'::double precision) >= 0
                        WHEN 'decrease' THEN (metric->>'effectValue')::double precision <= -hypothesis.minimum_effect
                          AND COALESCE((metric->>'upperConfidenceBound')::double precision, 'Infinity'::double precision) <= 0
                        ELSE false END
                      WHEN 'falsifies' THEN CASE hypothesis.expected_effect_direction
                        WHEN 'increase' THEN COALESCE((metric->>'upperConfidenceBound')::double precision, 'Infinity'::double precision) < hypothesis.minimum_effect
                        WHEN 'decrease' THEN COALESCE((metric->>'lowerConfidenceBound')::double precision, '-Infinity'::double precision) > -hypothesis.minimum_effect
                        ELSE false END
                      ELSE false END)
                AND NOT EXISTS (
                  SELECT 1 FROM mechanism_claim_variables claim_variable
                  WHERE claim_variable.claim_id = @claim_id
                    AND claim_variable.claim_version = @claim_version
                    AND NOT EXISTS (
                      SELECT 1 FROM research_hypothesis_variables hypothesis_variable
                      WHERE hypothesis_variable.hypothesis_id = hypothesis.hypothesis_id
                        AND hypothesis_variable.variable_code = claim_variable.variable_code));
            """);
        command.Parameters.AddWithValue("result_id", resultId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("hypothesis_id", validationHypothesisId);
        command.Parameters.AddWithValue("claim_id", claim.ClaimId);
        command.Parameters.AddWithValue("claim_version", claim.Version);
        command.Parameters.AddWithValue("hash", evidence.ContentHash);
        command.Parameters.AddWithValue("evaluation_outcome", evaluationOutcome);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
    }

    public async Task<MechanismClaimVersion> AddReviewAsync(
        MechanismClaimReview review,
        string targetStatus,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO mechanism_claim_reviews(
              review_id, claim_id, claim_version, decision, reviewer_id, comment, reviewed_at)
            VALUES (@id, @claim_id, @version, @decision, @reviewer, @comment, @reviewed_at);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", review.ReviewId);
            insert.Parameters.AddWithValue("claim_id", review.ClaimId);
            insert.Parameters.AddWithValue("version", review.ClaimVersion);
            insert.Parameters.AddWithValue("decision", review.Decision);
            insert.Parameters.AddWithValue("reviewer", review.ReviewerId);
            AddNullable(insert, "comment", NpgsqlDbType.Text, review.Comment);
            insert.Parameters.AddWithValue("reviewed_at", review.ReviewedAt);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var updateVersion = new NpgsqlCommand(
            """
            UPDATE mechanism_claim_versions SET reviewed_by = @reviewer, reviewed_at = @reviewed_at
            WHERE claim_id = @claim_id AND version = @version;
            """, connection, transaction))
        {
            updateVersion.Parameters.AddWithValue("claim_id", review.ClaimId);
            updateVersion.Parameters.AddWithValue("version", review.ClaimVersion);
            updateVersion.Parameters.AddWithValue("reviewer", review.ReviewerId);
            updateVersion.Parameters.AddWithValue("reviewed_at", review.ReviewedAt);
            await updateVersion.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var updateClaim = new NpgsqlCommand(
            """
            UPDATE mechanism_claims SET status = @status, updated_at = @updated_at
            WHERE claim_id = @claim_id AND current_version = @version;
            """, connection, transaction))
        {
            updateClaim.Parameters.AddWithValue("claim_id", review.ClaimId);
            updateClaim.Parameters.AddWithValue("version", review.ClaimVersion);
            updateClaim.Parameters.AddWithValue("status", targetStatus);
            updateClaim.Parameters.AddWithValue("updated_at", review.ReviewedAt);
            if (await updateClaim.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("机理声明版本已变化，不能应用审核结果。");
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await ReadClaimAsync(connection, review.ClaimId, review.ClaimVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("机理声明审核后无法读取。");
    }

    public async Task<MechanismClaimConflict> AddConflictAsync(
        MechanismClaimConflict value,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO mechanism_claim_conflicts(
              conflict_id, project_id, left_claim_id, left_claim_version,
              right_claim_id, right_claim_version, conflict_kind, rationale,
              status, created_by, created_at)
            VALUES (
              @id, @project_id, @left_id, @left_version,
              @right_id, @right_version, @kind, @rationale,
              @status, @created_by, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.ConflictId);
        command.Parameters.AddWithValue("project_id", value.ProjectId);
        command.Parameters.AddWithValue("left_id", value.LeftClaimId);
        command.Parameters.AddWithValue("left_version", value.LeftClaimVersion);
        command.Parameters.AddWithValue("right_id", value.RightClaimId);
        command.Parameters.AddWithValue("right_version", value.RightClaimVersion);
        command.Parameters.AddWithValue("kind", value.ConflictKind);
        command.Parameters.AddWithValue("rationale", value.Rationale);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("created_by", value.CreatedBy);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("相同声明之间已经存在未解决冲突，请刷新后处理。", exception);
        }
        return value;
    }

    public async Task<MechanismClaimConflict?> GetConflictAsync(
        Guid conflictId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT conflict_id, project_id, left_claim_id, left_claim_version,
              right_claim_id, right_claim_version, conflict_kind, rationale,
              status, created_by, created_at, resolved_by, resolved_at, resolution
            FROM mechanism_claim_conflicts WHERE conflict_id = @id;
            """);
        command.Parameters.AddWithValue("id", conflictId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadConflict(reader) : null;
    }

    public async Task<MechanismClaimConflict> ResolveConflictAsync(
        MechanismClaimConflict value,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE mechanism_claim_conflicts
            SET status='resolved', resolved_by=@resolved_by, resolved_at=@resolved_at, resolution=@resolution
            WHERE conflict_id=@id AND project_id=@project_id AND status='open';
            """);
        command.Parameters.AddWithValue("id", value.ConflictId);
        command.Parameters.AddWithValue("project_id", value.ProjectId);
        command.Parameters.AddWithValue("resolved_by", value.ResolvedBy!);
        command.Parameters.AddWithValue("resolved_at", value.ResolvedAt!.Value);
        command.Parameters.AddWithValue("resolution", value.Resolution!);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("机理冲突已被其他人员解决或状态已变化。");
        return value;
    }

    public async Task<IReadOnlyList<MechanismClaimConflict>> ListConflictsAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT conflict_id, project_id, left_claim_id, left_claim_version,
              right_claim_id, right_claim_version, conflict_kind, rationale,
              status, created_by, created_at, resolved_by, resolved_at, resolution
            FROM mechanism_claim_conflicts
            WHERE project_id = @project_id
            ORDER BY created_at DESC;
            """);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<MechanismClaimConflict>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(ReadConflict(reader));
        return values;
    }

    private static MechanismClaimConflict ReadConflict(NpgsqlDataReader reader) => new()
    {
        ConflictId = reader.GetGuid(0), ProjectId = reader.GetGuid(1),
        LeftClaimId = reader.GetGuid(2), LeftClaimVersion = reader.GetInt32(3),
        RightClaimId = reader.GetGuid(4), RightClaimVersion = reader.GetInt32(5),
        ConflictKind = reader.GetString(6), Rationale = reader.GetString(7),
        Status = reader.GetString(8), CreatedBy = reader.GetString(9),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(10),
        ResolvedBy = reader.IsDBNull(11) ? null : reader.GetString(11),
        ResolvedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
        Resolution = reader.IsDBNull(13) ? null : reader.GetString(13)
    };

    public async Task SaveUsagesAsync(
        IReadOnlyList<MechanismClaimUsage> values,
        CancellationToken ct = default)
    {
        if (values.Count == 0) return;
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var value in values)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO recommendation_knowledge_usage(
                  recommendation_id, claim_id, claim_version, usage_type, content_hash)
                VALUES (@recommendation_id, @claim_id, @claim_version, @usage_type, @content_hash)
                ON CONFLICT DO NOTHING;
                """, connection, transaction);
            command.Parameters.AddWithValue("recommendation_id", value.RecommendationId);
            command.Parameters.AddWithValue("claim_id", value.ClaimId);
            command.Parameters.AddWithValue("claim_version", value.ClaimVersion);
            command.Parameters.AddWithValue("usage_type", value.UsageType);
            command.Parameters.AddWithValue("content_hash", value.ContentHash);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT usage.recommendation_id, usage.claim_id, usage.claim_version,
              usage.usage_type, usage.content_hash, version.name
            FROM recommendation_knowledge_usage usage
            JOIN mechanism_claims claim ON claim.claim_id = usage.claim_id
            JOIN mechanism_claim_versions version
              ON version.claim_id = usage.claim_id AND version.version = usage.claim_version
            WHERE claim.project_id = @project_id
            ORDER BY usage.recommendation_id, version.name, usage.usage_type;
            """);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<MechanismClaimUsage>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(new MechanismClaimUsage
            {
                RecommendationId = reader.GetGuid(0),
                ClaimId = reader.GetGuid(1),
                ClaimVersion = reader.GetInt32(2),
                UsageType = reader.GetString(3),
                ContentHash = reader.GetString(4),
                ClaimName = reader.GetString(5)
            });
        return values;
    }

    public async Task<bool> LifecycleEvidenceUsedAsync(
        Guid claimId,
        string referenceId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS(
              SELECT 1 FROM mechanism_claim_lifecycle_decisions
              WHERE claim_id = @claim_id AND reference_id = @reference_id);
            """);
        command.Parameters.AddWithValue("claim_id", claimId);
        command.Parameters.AddWithValue("reference_id", referenceId);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
    }

    public async Task<bool> LifecycleActorUsedAsync(
        Guid claimId,
        string userId,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT COALESCE((
              SELECT decided_by = @user_id FROM mechanism_claim_lifecycle_decisions
              WHERE claim_id = @claim_id ORDER BY decided_at DESC LIMIT 1), false);
            """);
        command.Parameters.AddWithValue("claim_id", claimId);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? false);
    }

    public async Task<MechanismClaimVersion> TransitionAsync(
        MechanismClaimLifecycleDecision decision,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var update = new NpgsqlCommand(
            """
            UPDATE mechanism_claims SET status = @to_status, updated_at = @decided_at
            WHERE claim_id = @claim_id AND current_version = @claim_version AND status = @from_status;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("claim_id", decision.ClaimId);
            update.Parameters.AddWithValue("claim_version", decision.ClaimVersion);
            update.Parameters.AddWithValue("from_status", decision.FromStatus);
            update.Parameters.AddWithValue("to_status", decision.ToStatus);
            update.Parameters.AddWithValue("decided_at", decision.DecidedAt);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("机理声明状态已变化，不能应用当前生命周期决定。");
        }
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO mechanism_claim_lifecycle_decisions(
              decision_id, claim_id, claim_version, from_status, to_status,
              evidence_kind, reference_id, content_hash, validation_hypothesis_id,
              evaluation_outcome, evaluation_summary, comment, decided_by, decided_at)
            VALUES (@decision_id, @claim_id, @claim_version, @from_status, @to_status,
              @evidence_kind, @reference_id, @content_hash, @validation_hypothesis_id,
              @evaluation_outcome, @evaluation_summary, @comment, @decided_by, @decided_at);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("decision_id", decision.DecisionId);
            insert.Parameters.AddWithValue("claim_id", decision.ClaimId);
            insert.Parameters.AddWithValue("claim_version", decision.ClaimVersion);
            insert.Parameters.AddWithValue("from_status", decision.FromStatus);
            insert.Parameters.AddWithValue("to_status", decision.ToStatus);
            AddNullable(insert, "evidence_kind", NpgsqlDbType.Text, decision.EvidenceKind);
            AddNullable(insert, "reference_id", NpgsqlDbType.Text, decision.ReferenceId);
            AddNullable(insert, "content_hash", NpgsqlDbType.Text, decision.ContentHash);
            AddNullable(insert, "validation_hypothesis_id", NpgsqlDbType.Uuid, decision.ValidationHypothesisId);
            AddNullable(insert, "evaluation_outcome", NpgsqlDbType.Text, decision.EvaluationOutcome);
            AddNullable(insert, "evaluation_summary", NpgsqlDbType.Text, decision.EvaluationSummary);
            AddNullable(insert, "comment", NpgsqlDbType.Text, decision.Comment);
            insert.Parameters.AddWithValue("decided_by", decision.DecidedBy);
            insert.Parameters.AddWithValue("decided_at", decision.DecidedAt);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await ReadClaimAsync(connection, decision.ClaimId, decision.ClaimVersion, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("机理声明状态更新后无法读取。");
    }

    private static async Task<MechanismClaimVersion?> ReadClaimAsync(
        NpgsqlConnection connection, Guid claimId, int? requestedVersion, CancellationToken ct)
    {
        MechanismClaimVersion? value;
        await using (var command = new NpgsqlCommand(
            """
            SELECT c.project_id, v.version, c.status, v.name, v.mechanism_type,
              v.statement, v.expected_signature, v.falsification_condition, v.evidence_level,
              v.created_by, v.created_at, v.reviewed_by, v.reviewed_at, v.content_hash, c.updated_at
            FROM mechanism_claims c
            JOIN mechanism_claim_versions v ON v.claim_id = c.claim_id
              AND v.version = COALESCE(@version, c.current_version)
            WHERE c.claim_id = @claim_id;
            """, connection))
        {
            command.Parameters.AddWithValue("claim_id", claimId);
            AddNullable(command, "version", NpgsqlDbType.Integer, requestedVersion);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            value = new MechanismClaimVersion
            {
                ClaimId = claimId, ProjectId = reader.GetGuid(0), Version = reader.GetInt32(1),
                Status = reader.GetString(2), Name = reader.GetString(3), MechanismType = reader.GetString(4),
                Statement = reader.GetString(5), ExpectedSignature = reader.IsDBNull(6) ? null : reader.GetString(6),
                FalsificationCondition = reader.GetString(7), EvidenceLevel = reader.GetString(8),
                CreatedBy = reader.GetString(9), CreatedAt = reader.GetFieldValue<DateTimeOffset>(10),
                ReviewedBy = reader.IsDBNull(11) ? null : reader.GetString(11),
                ReviewedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                ContentHash = reader.GetString(13), UpdatedAt = reader.GetFieldValue<DateTimeOffset>(14)
            };
        }
        var variables = await ReadVariablesAsync(connection, claimId, value.Version, ct).ConfigureAwait(false);
        var applicability = await ReadApplicabilityAsync(connection, claimId, value.Version, ct).ConfigureAwait(false);
        var constraints = await ReadConstraintsAsync(connection, claimId, value.Version, ct).ConfigureAwait(false);
        var evidence = await ReadEvidenceAsync(connection, claimId, value.Version, ct).ConfigureAwait(false);
        return value with { Variables = variables, Applicability = applicability, Constraints = constraints, Evidence = evidence };
    }

    private static async Task InsertVariableAsync(NpgsqlConnection c, NpgsqlTransaction t, MechanismClaimVersion claim, MechanismClaimVariable value, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO mechanism_claim_variables VALUES (@id,@version,@code,@role,@direction,@delay,@unit);", c, t);
        command.Parameters.AddWithValue("id", claim.ClaimId); command.Parameters.AddWithValue("version", claim.Version);
        command.Parameters.AddWithValue("code", value.VariableCode); command.Parameters.AddWithValue("role", value.VariableRole);
        AddNullable(command, "direction", NpgsqlDbType.Text, value.Direction);
        AddNullable(command, "delay", NpgsqlDbType.Bigint, value.DelayMilliseconds); command.Parameters.AddWithValue("unit", value.Unit);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertApplicabilityAsync(NpgsqlConnection c, NpgsqlTransaction t, MechanismClaimVersion claim, MechanismClaimApplicability value, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO mechanism_claim_applicability VALUES (@id,@version,@code,@value);", c, t);
        command.Parameters.AddWithValue("id", claim.ClaimId); command.Parameters.AddWithValue("version", claim.Version);
        command.Parameters.AddWithValue("code", value.DimensionCode); command.Parameters.AddWithValue("value", value.DimensionValue);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertConstraintAsync(NpgsqlConnection c, NpgsqlTransaction t, MechanismClaimVersion claim, MechanismClaimConstraint value, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO mechanism_claim_constraints VALUES (@constraint_id,@id,@version,@code,@kind,@minimum,@maximum,@unit,@severity);", c, t);
        command.Parameters.AddWithValue("constraint_id", value.ConstraintId); command.Parameters.AddWithValue("id", claim.ClaimId);
        command.Parameters.AddWithValue("version", claim.Version); command.Parameters.AddWithValue("code", value.VariableCode);
        command.Parameters.AddWithValue("kind", value.ConstraintKind); AddNullable(command, "minimum", NpgsqlDbType.Double, value.Minimum);
        AddNullable(command, "maximum", NpgsqlDbType.Double, value.Maximum); command.Parameters.AddWithValue("unit", value.Unit);
        command.Parameters.AddWithValue("severity", value.Severity); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertEvidenceAsync(NpgsqlConnection c, NpgsqlTransaction t, MechanismClaimVersion claim, MechanismClaimEvidence value, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("INSERT INTO mechanism_claim_evidence VALUES (@evidence_id,@id,@version,@kind,@reference,@polarity,@hash,@created_at);", c, t);
        command.Parameters.AddWithValue("evidence_id", value.EvidenceLinkId); command.Parameters.AddWithValue("id", claim.ClaimId);
        command.Parameters.AddWithValue("version", claim.Version); command.Parameters.AddWithValue("kind", value.EvidenceKind);
        command.Parameters.AddWithValue("reference", value.ReferenceId); command.Parameters.AddWithValue("polarity", value.Polarity);
        command.Parameters.AddWithValue("hash", value.ContentHash); command.Parameters.AddWithValue("created_at", claim.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MechanismClaimVariable>> ReadVariablesAsync(NpgsqlConnection c, Guid id, int version, CancellationToken ct)
    {
        await using var command = ChildCommand(c, "SELECT variable_code, variable_role, direction, delay_ms, unit FROM mechanism_claim_variables WHERE claim_id=@id AND claim_version=@version ORDER BY variable_role, variable_code;", id, version);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var values = new List<MechanismClaimVariable>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new MechanismClaimVariable { VariableCode=reader.GetString(0), VariableRole=reader.GetString(1), Direction=reader.IsDBNull(2)?null:reader.GetString(2), DelayMilliseconds=reader.IsDBNull(3)?null:reader.GetInt64(3), Unit=reader.GetString(4) });
        return values;
    }

    private static async Task<IReadOnlyList<MechanismClaimApplicability>> ReadApplicabilityAsync(NpgsqlConnection c, Guid id, int version, CancellationToken ct)
    {
        await using var command = ChildCommand(c, "SELECT dimension_code, dimension_value FROM mechanism_claim_applicability WHERE claim_id=@id AND claim_version=@version ORDER BY dimension_code, dimension_value;", id, version);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var values = new List<MechanismClaimApplicability>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new MechanismClaimApplicability { DimensionCode=reader.GetString(0), DimensionValue=reader.GetString(1) });
        return values;
    }

    private static async Task<IReadOnlyList<MechanismClaimConstraint>> ReadConstraintsAsync(NpgsqlConnection c, Guid id, int version, CancellationToken ct)
    {
        await using var command = ChildCommand(c, "SELECT constraint_id, variable_code, constraint_kind, minimum, maximum, unit, severity FROM mechanism_claim_constraints WHERE claim_id=@id AND claim_version=@version ORDER BY variable_code;", id, version);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var values = new List<MechanismClaimConstraint>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new MechanismClaimConstraint { ConstraintId=reader.GetGuid(0), VariableCode=reader.GetString(1), ConstraintKind=reader.GetString(2), Minimum=reader.IsDBNull(3)?null:reader.GetDouble(3), Maximum=reader.IsDBNull(4)?null:reader.GetDouble(4), Unit=reader.GetString(5), Severity=reader.GetString(6) });
        return values;
    }

    private static async Task<IReadOnlyList<MechanismClaimEvidence>> ReadEvidenceAsync(NpgsqlConnection c, Guid id, int version, CancellationToken ct)
    {
        await using var command = ChildCommand(c, "SELECT evidence_link_id, evidence_kind, reference_id, polarity, content_hash FROM mechanism_claim_evidence WHERE claim_id=@id AND claim_version=@version ORDER BY created_at;", id, version);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var values = new List<MechanismClaimEvidence>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new MechanismClaimEvidence { EvidenceLinkId=reader.GetGuid(0), EvidenceKind=reader.GetString(1), ReferenceId=reader.GetString(2), Polarity=reader.GetString(3), ContentHash=reader.GetString(4) });
        return values;
    }

    private static NpgsqlCommand ChildCommand(NpgsqlConnection c, string sql, Guid id, int version)
    {
        var command = new NpgsqlCommand(sql, c); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("version", version); return command;
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
}
