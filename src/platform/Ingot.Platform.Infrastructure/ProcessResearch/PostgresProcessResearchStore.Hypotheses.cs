// 持久化研究假设及其结构化子项。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore
{
    public Task<ResearchHypothesis?> GetHypothesisAsync(
        Guid hypothesisId,
        CancellationToken ct = default)
        => ReadHypothesisAsync(hypothesisId, ct);

    public async Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var values = new List<ResearchHypothesis>();
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
            """
            SELECT hypothesis_id, project_id, statement, rationale, status, validation_outcome_code,
              expected_effect_direction, minimum_effect, applicability, confidence,
              created_by, created_at, updated_at
            FROM research_hypotheses
            WHERE project_id=@project_id
            ORDER BY updated_at DESC, hypothesis_id;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                values.Add(new ResearchHypothesis
                {
                    HypothesisId = reader.GetGuid(0),
                    ProjectId = reader.GetGuid(1),
                    Statement = reader.GetString(2),
                    Rationale = reader.GetString(3),
                    Status = reader.GetString(4),
                    ValidationOutcomeCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ExpectedEffectDirection = reader.IsDBNull(6) ? null : reader.GetString(6),
                    MinimumEffect = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    Applicability = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Confidence = reader.GetDouble(9),
                    CreatedBy = reader.GetString(10),
                    CreatedAt = reader.GetFieldValue<DateTimeOffset>(11),
                    UpdatedAt = reader.GetFieldValue<DateTimeOffset>(12)
                });
        }
        if (values.Count == 0) return values;

        var children = values.ToDictionary(
            static value => value.HypothesisId,
            static _ => new HypothesisChildren());
        await using (var command = new NpgsqlCommand(
            """
            WITH requested AS (
              SELECT hypothesis_id FROM research_hypotheses WHERE project_id=@project_id
            )
            SELECT item.hypothesis_id, item.kind, item.role, item.sequence, item.payload::text
            FROM (
              SELECT child.hypothesis_id, 'variable'::text kind, ''::text role, child.sequence,
                to_jsonb(child.variable_code) payload
              FROM research_hypothesis_variables child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'confounder', '', child.sequence, to_jsonb(child.description)
              FROM research_hypothesis_confounders child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'causal-link', '', child.sequence,
                jsonb_build_object('fromVariableCode',child.from_variable_code,'toVariableCode',child.to_variable_code,
                  'mechanism',child.mechanism,'direction',child.direction)
              FROM research_hypothesis_causal_links child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'temporal-feature', '', child.sequence,
                jsonb_build_object('variableCode',child.variable_code,'featureCode',child.feature_code,
                  'phaseCode',child.phase_code,'delayMilliseconds',child.delay_ms,'windowMilliseconds',child.window_ms)
              FROM research_hypothesis_temporal_features child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'interaction', '', child.sequence,
                jsonb_build_object('description',child.description,'variableCodes',COALESCE((
                  SELECT jsonb_agg(variable.variable_code ORDER BY variable.sequence)
                  FROM research_hypothesis_interaction_variables variable
                  WHERE variable.interaction_id=child.interaction_id),'[]'::jsonb))
              FROM research_hypothesis_interactions child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'failure-condition', '', child.sequence,
                jsonb_build_object('condition',child.condition,'observableSignal',child.observable_signal,
                  'requiredResponse',child.required_response)
              FROM research_hypothesis_failure_conditions child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'falsification-condition', '', child.sequence, to_jsonb(child.condition)
              FROM research_hypothesis_falsification_conditions child JOIN requested USING(hypothesis_id)
              UNION ALL
              SELECT child.hypothesis_id, 'evidence', child.evidence_role,
                row_number() OVER (PARTITION BY child.hypothesis_id,child.evidence_role ORDER BY child.created_at,child.evidence_id)::integer,
                jsonb_build_object('evidenceId',child.evidence_id,'projectId',child.project_id,'kind',child.kind,
                  'referenceId',child.reference_id,'summary',child.summary,'contentHash',child.content_hash,
                  'createdAt',child.created_at)
              FROM research_hypothesis_evidence child JOIN requested USING(hypothesis_id)
            ) item
            ORDER BY item.hypothesis_id, item.kind, item.role, item.sequence;
            """, connection))
        {
            command.Parameters.AddWithValue("project_id", projectId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var current = children[reader.GetGuid(0)];
                var kind = reader.GetString(1);
                var role = reader.GetString(2);
                var payload = reader.GetString(4);
                switch (kind)
                {
                    case "variable": current.VariableCodes.Add(Deserialize<string>(payload)); break;
                    case "confounder": current.PossibleConfounders.Add(Deserialize<string>(payload)); break;
                    case "causal-link": current.CausalChain.Add(Deserialize<ResearchHypothesisCausalLink>(payload)); break;
                    case "temporal-feature": current.TemporalFeatures.Add(Deserialize<ResearchHypothesisTemporalFeature>(payload)); break;
                    case "interaction": current.Interactions.Add(Deserialize<ResearchHypothesisInteraction>(payload)); break;
                    case "failure-condition": current.FailureConditions.Add(Deserialize<ResearchHypothesisFailureCondition>(payload)); break;
                    case "falsification-condition": current.FalsificationConditions.Add(Deserialize<string>(payload)); break;
                    case "evidence" when role == "supporting": current.SupportingEvidence.Add(Deserialize<EvidenceReference>(payload)); break;
                    case "evidence" when role == "opposing": current.OpposingEvidence.Add(Deserialize<EvidenceReference>(payload)); break;
                    case "evidence" when role == "validation": current.ValidationEvidence.Add(Deserialize<EvidenceReference>(payload)); break;
                }
            }
        }
        return values.Select(value =>
        {
            var child = children[value.HypothesisId];
            return value with
            {
                VariableCodes = child.VariableCodes,
                PossibleConfounders = child.PossibleConfounders,
                CausalChain = child.CausalChain,
                TemporalFeatures = child.TemporalFeatures,
                Interactions = child.Interactions,
                FailureConditions = child.FailureConditions,
                FalsificationConditions = child.FalsificationConditions,
                SupportingEvidence = child.SupportingEvidence,
                OpposingEvidence = child.OpposingEvidence,
                ValidationEvidence = child.ValidationEvidence
            };
        }).ToArray();
    }

    private sealed class HypothesisChildren
    {
        public List<string> VariableCodes { get; } = [];
        public List<string> PossibleConfounders { get; } = [];
        public List<ResearchHypothesisCausalLink> CausalChain { get; } = [];
        public List<ResearchHypothesisTemporalFeature> TemporalFeatures { get; } = [];
        public List<ResearchHypothesisInteraction> Interactions { get; } = [];
        public List<ResearchHypothesisFailureCondition> FailureConditions { get; } = [];
        public List<string> FalsificationConditions { get; } = [];
        public List<EvidenceReference> SupportingEvidence { get; } = [];
        public List<EvidenceReference> OpposingEvidence { get; } = [];
        public List<EvidenceReference> ValidationEvidence { get; } = [];
    }

    public async Task<ResearchHypothesis> SaveHypothesisAsync(
        ResearchHypothesis value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO research_hypotheses(
              hypothesis_id, project_id, status, statement, rationale,
              validation_outcome_code, expected_effect_direction, minimum_effect,
              applicability, confidence, created_by, created_at, updated_at)
            VALUES (
              @id, @project_id, @status, @statement, @rationale,
              @outcome, @direction, @minimum_effect,
              @applicability, @confidence, @created_by, @created_at, @updated_at)
            ON CONFLICT (hypothesis_id) DO UPDATE SET
              status = EXCLUDED.status,
              statement = EXCLUDED.statement,
              rationale = EXCLUDED.rationale,
              validation_outcome_code = EXCLUDED.validation_outcome_code,
              expected_effect_direction = EXCLUDED.expected_effect_direction,
              minimum_effect = EXCLUDED.minimum_effect,
              applicability = EXCLUDED.applicability,
              confidence = EXCLUDED.confidence,
              updated_at = EXCLUDED.updated_at
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", value.HypothesisId);
            command.Parameters.AddWithValue("project_id", value.ProjectId);
            command.Parameters.AddWithValue("status", value.Status);
            command.Parameters.AddWithValue("statement", value.Statement);
            command.Parameters.AddWithValue("rationale", value.Rationale);
            AddNullable(command, "outcome", NpgsqlDbType.Text, value.ValidationOutcomeCode);
            AddNullable(command, "direction", NpgsqlDbType.Text, value.ExpectedEffectDirection);
            AddNullable(command, "minimum_effect", NpgsqlDbType.Double, value.MinimumEffect);
            AddNullable(command, "applicability", NpgsqlDbType.Text, value.Applicability);
            command.Parameters.AddWithValue("confidence", value.Confidence);
            command.Parameters.AddWithValue("created_by", value.CreatedBy);
            command.Parameters.AddWithValue("created_at", value.CreatedAt);
            command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await ReplaceHypothesisChildrenAsync(connection, transaction, value, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    private async Task<ResearchHypothesis?> ReadHypothesisAsync(Guid hypothesisId, CancellationToken ct)
    {
        ResearchHypothesis? value;
        await using (var command = _dataSource.CreateCommand(
            """
            SELECT project_id, statement, rationale, status, validation_outcome_code,
              expected_effect_direction, minimum_effect, applicability, confidence,
              created_by, created_at, updated_at
            FROM research_hypotheses WHERE hypothesis_id=@id;
            """))
        {
            command.Parameters.AddWithValue("id", hypothesisId);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            value = new ResearchHypothesis
            {
                HypothesisId = hypothesisId,
                ProjectId = reader.GetGuid(0),
                Statement = reader.GetString(1),
                Rationale = reader.GetString(2),
                Status = reader.GetString(3),
                ValidationOutcomeCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                ExpectedEffectDirection = reader.IsDBNull(5) ? null : reader.GetString(5),
                MinimumEffect = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                Applicability = reader.IsDBNull(7) ? null : reader.GetString(7),
                Confidence = reader.GetDouble(8),
                CreatedBy = reader.GetString(9),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(10),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(11)
            };
        }
        return value with
        {
            VariableCodes = await ReadHypothesisTextListAsync("research_hypothesis_variables", "variable_code", hypothesisId, ct).ConfigureAwait(false),
            PossibleConfounders = await ReadHypothesisTextListAsync("research_hypothesis_confounders", "description", hypothesisId, ct).ConfigureAwait(false),
            CausalChain = await ReadCausalChainAsync(hypothesisId, ct).ConfigureAwait(false),
            TemporalFeatures = await ReadTemporalFeaturesAsync(hypothesisId, ct).ConfigureAwait(false),
            Interactions = await ReadInteractionsAsync(hypothesisId, ct).ConfigureAwait(false),
            FailureConditions = await ReadFailureConditionsAsync(hypothesisId, ct).ConfigureAwait(false),
            FalsificationConditions = await ReadHypothesisTextListAsync("research_hypothesis_falsification_conditions", "condition", hypothesisId, ct).ConfigureAwait(false),
            SupportingEvidence = await ReadHypothesisEvidenceAsync(hypothesisId, "supporting", ct).ConfigureAwait(false),
            OpposingEvidence = await ReadHypothesisEvidenceAsync(hypothesisId, "opposing", ct).ConfigureAwait(false),
            ValidationEvidence = await ReadHypothesisEvidenceAsync(hypothesisId, "validation", ct).ConfigureAwait(false)
        };
    }

    private async Task<IReadOnlyList<string>> ReadHypothesisTextListAsync(
        string table, string column, Guid hypothesisId, CancellationToken ct)
    {
        var allowed = (table, column) is
            ("research_hypothesis_variables", "variable_code") or
            ("research_hypothesis_confounders", "description") or
            ("research_hypothesis_falsification_conditions", "condition");
        if (!allowed) throw new InvalidOperationException("不允许读取未注册的假设子表。");
        await using var command = _dataSource.CreateCommand(
            $"SELECT {column} FROM {table} WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", hypothesisId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<string>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(reader.GetString(0));
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisCausalLink>> ReadCausalChainAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT from_variable_code,to_variable_code,mechanism,direction FROM research_hypothesis_causal_links WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<ResearchHypothesisCausalLink>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new ResearchHypothesisCausalLink { FromVariableCode = reader.GetString(0), ToVariableCode = reader.GetString(1), Mechanism = reader.GetString(2), Direction = reader.IsDBNull(3) ? null : reader.GetString(3) });
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisTemporalFeature>> ReadTemporalFeaturesAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT variable_code,feature_code,phase_code,delay_ms,window_ms FROM research_hypothesis_temporal_features WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<ResearchHypothesisTemporalFeature>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new ResearchHypothesisTemporalFeature { VariableCode = reader.GetString(0), FeatureCode = reader.GetString(1), PhaseCode = reader.IsDBNull(2) ? null : reader.GetString(2), DelayMilliseconds = reader.IsDBNull(3) ? null : reader.GetInt64(3), WindowMilliseconds = reader.IsDBNull(4) ? null : reader.GetInt64(4) });
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisInteraction>> ReadInteractionsAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT interaction_id,description FROM research_hypothesis_interactions WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<(Guid Id, string Description)>(); while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add((reader.GetGuid(0), reader.GetString(1)));
        var values = new List<ResearchHypothesisInteraction>();
        foreach (var row in rows)
        {
            await using var variables = _dataSource.CreateCommand("SELECT variable_code FROM research_hypothesis_interaction_variables WHERE interaction_id=@id ORDER BY sequence;");
            variables.Parameters.AddWithValue("id", row.Id); await using var variableReader = await variables.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var codes = new List<string>(); while (await variableReader.ReadAsync(ct).ConfigureAwait(false)) codes.Add(variableReader.GetString(0));
            values.Add(new ResearchHypothesisInteraction { VariableCodes = codes, Description = row.Description });
        }
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisFailureCondition>> ReadFailureConditionsAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT condition,observable_signal,required_response FROM research_hypothesis_failure_conditions WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<ResearchHypothesisFailureCondition>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new ResearchHypothesisFailureCondition { Condition = reader.GetString(0), ObservableSignal = reader.GetString(1), RequiredResponse = reader.GetString(2) });
        return values;
    }

    private async Task<IReadOnlyList<EvidenceReference>> ReadHypothesisEvidenceAsync(Guid id, string role, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT evidence_id,project_id,kind,reference_id,summary,content_hash,created_at FROM research_hypothesis_evidence WHERE hypothesis_id=@id AND evidence_role=@role ORDER BY created_at,evidence_id;");
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("role", role);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var values = new List<EvidenceReference>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new EvidenceReference { EvidenceId = reader.GetGuid(0), ProjectId = reader.GetGuid(1), Kind = reader.GetString(2), ReferenceId = reader.GetString(3), Summary = reader.GetString(4), ContentHash = reader.GetString(5), CreatedAt = reader.GetFieldValue<DateTimeOffset>(6) });
        return values;
    }

    private static async Task ReplaceHypothesisChildrenAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ResearchHypothesis value, CancellationToken ct)
    {
        foreach (var table in new[] { "research_hypothesis_variables", "research_hypothesis_confounders", "research_hypothesis_causal_links", "research_hypothesis_temporal_features", "research_hypothesis_interactions", "research_hypothesis_failure_conditions", "research_hypothesis_falsification_conditions", "research_hypothesis_evidence" })
        {
            await using var delete = new NpgsqlCommand($"DELETE FROM {table} WHERE hypothesis_id=@id;", connection, transaction);
            delete.Parameters.AddWithValue("id", value.HypothesisId); await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        for (var i = 0; i < value.VariableCodes.Count; i++) await InsertSimpleChildAsync(connection, transaction, "research_hypothesis_variables", "variable_code", value.HypothesisId, i, value.VariableCodes[i], ct).ConfigureAwait(false);
        for (var i = 0; i < value.PossibleConfounders.Count; i++) await InsertSimpleChildAsync(connection, transaction, "research_hypothesis_confounders", "description", value.HypothesisId, i, value.PossibleConfounders[i], ct).ConfigureAwait(false);
        for (var i = 0; i < value.FalsificationConditions.Count; i++) await InsertSimpleChildAsync(connection, transaction, "research_hypothesis_falsification_conditions", "condition", value.HypothesisId, i, value.FalsificationConditions[i], ct).ConfigureAwait(false);
        for (var i = 0; i < value.CausalChain.Count; i++)
        {
            var item = value.CausalChain[i]; await using var command = new NpgsqlCommand("INSERT INTO research_hypothesis_causal_links VALUES (@id,@sequence,@from,@to,@mechanism,@direction);", connection, transaction);
            command.Parameters.AddWithValue("id", value.HypothesisId); command.Parameters.AddWithValue("sequence", i); command.Parameters.AddWithValue("from", item.FromVariableCode); command.Parameters.AddWithValue("to", item.ToVariableCode); command.Parameters.AddWithValue("mechanism", item.Mechanism); AddNullable(command, "direction", NpgsqlDbType.Text, item.Direction); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        for (var i = 0; i < value.TemporalFeatures.Count; i++)
        {
            var item = value.TemporalFeatures[i]; await using var command = new NpgsqlCommand("INSERT INTO research_hypothesis_temporal_features VALUES (@id,@sequence,@variable,@feature,@phase,@delay,@window);", connection, transaction);
            command.Parameters.AddWithValue("id", value.HypothesisId); command.Parameters.AddWithValue("sequence", i); command.Parameters.AddWithValue("variable", item.VariableCode); command.Parameters.AddWithValue("feature", item.FeatureCode); AddNullable(command, "phase", NpgsqlDbType.Text, item.PhaseCode); AddNullable(command, "delay", NpgsqlDbType.Bigint, item.DelayMilliseconds); AddNullable(command, "window", NpgsqlDbType.Bigint, item.WindowMilliseconds); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        for (var i = 0; i < value.Interactions.Count; i++)
        {
            var item = value.Interactions[i]; var interactionId = Guid.CreateVersion7(); await using var command = new NpgsqlCommand("INSERT INTO research_hypothesis_interactions VALUES (@interaction_id,@id,@sequence,@description);", connection, transaction);
            command.Parameters.AddWithValue("interaction_id", interactionId); command.Parameters.AddWithValue("id", value.HypothesisId); command.Parameters.AddWithValue("sequence", i); command.Parameters.AddWithValue("description", item.Description); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            for (var j = 0; j < item.VariableCodes.Count; j++) { await using var variable = new NpgsqlCommand("INSERT INTO research_hypothesis_interaction_variables VALUES (@interaction_id,@sequence,@code);", connection, transaction); variable.Parameters.AddWithValue("interaction_id", interactionId); variable.Parameters.AddWithValue("sequence", j); variable.Parameters.AddWithValue("code", item.VariableCodes[j]); await variable.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        }
        for (var i = 0; i < value.FailureConditions.Count; i++) { var item = value.FailureConditions[i]; await using var command = new NpgsqlCommand("INSERT INTO research_hypothesis_failure_conditions VALUES (@failure_id,@id,@sequence,@condition,@signal,@response);", connection, transaction); command.Parameters.AddWithValue("failure_id", Guid.CreateVersion7()); command.Parameters.AddWithValue("id", value.HypothesisId); command.Parameters.AddWithValue("sequence", i); command.Parameters.AddWithValue("condition", item.Condition); command.Parameters.AddWithValue("signal", item.ObservableSignal); command.Parameters.AddWithValue("response", item.RequiredResponse); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        foreach (var pair in new[] { ("supporting", value.SupportingEvidence), ("opposing", value.OpposingEvidence), ("validation", value.ValidationEvidence) })
            foreach (var item in pair.Item2) { await using var command = new NpgsqlCommand("INSERT INTO research_hypothesis_evidence VALUES (@id,@evidence_id,@role,@project_id,@kind,@reference,@summary,@hash,@created_at);", connection, transaction); command.Parameters.AddWithValue("id", value.HypothesisId); command.Parameters.AddWithValue("evidence_id", item.EvidenceId); command.Parameters.AddWithValue("role", pair.Item1); command.Parameters.AddWithValue("project_id", item.ProjectId); command.Parameters.AddWithValue("kind", item.Kind); command.Parameters.AddWithValue("reference", item.ReferenceId); command.Parameters.AddWithValue("summary", item.Summary); command.Parameters.AddWithValue("hash", item.ContentHash); command.Parameters.AddWithValue("created_at", item.CreatedAt); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    }

    private static async Task InsertSimpleChildAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, string column, Guid id, int sequence, string value, CancellationToken ct)
    {
        var allowed = (table, column) is ("research_hypothesis_variables", "variable_code") or ("research_hypothesis_confounders", "description") or ("research_hypothesis_falsification_conditions", "condition");
        if (!allowed) throw new InvalidOperationException("不允许写入未注册的假设子表。");
        await using var command = new NpgsqlCommand($"INSERT INTO {table}(hypothesis_id,sequence,{column}) VALUES (@id,@sequence,@value);", connection, transaction); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("sequence", sequence); command.Parameters.AddWithValue("value", value); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

}
