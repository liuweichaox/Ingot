using Ingot.Platform.Application.ProcessResearch;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed class PostgresProcessResearchStore : IProcessResearchStore
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

    public Task<ResearchValidationPreregistration?> GetValidationPreregistrationAsync(
        Guid preregistrationId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchValidationPreregistration>(
            "SELECT payload FROM research_validation_preregistrations WHERE preregistration_id = $1",
            preregistrationId,
            ct);

    public Task<IReadOnlyList<ResearchValidationPreregistration>> ListValidationPreregistrationsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchValidationPreregistration>(
            """
            SELECT payload
            FROM research_validation_preregistrations
            WHERE project_id = $1
            ORDER BY version DESC, preregistration_id
            """,
            projectId,
            ct);

    public async Task<ResearchValidationPreregistration> CreateValidationPreregistrationAsync(
        ResearchValidationPreregistration value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO research_validation_preregistrations
              (preregistration_id, project_id, version, project_revision, status,
               project_snapshot_hash, content_hash, payload, frozen_at, reviewed_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, NULL)
            """);
        command.Parameters.AddWithValue(value.PreregistrationId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Version);
        command.Parameters.AddWithValue(value.ProjectRevision);
        command.Parameters.AddWithValue(value.Status);
        command.Parameters.AddWithValue(value.ProjectSnapshotHash);
        command.Parameters.AddWithValue(value.ContentHash);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.FrozenAt);
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return value;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ProcessResearchRuleException("该阶段 0 预注册版本或内容已经存在。");
        }
    }

    public async Task<ResearchValidationPreregistration> ReviewValidationPreregistrationAsync(
        ResearchValidationPreregistration value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE research_validation_preregistrations
            SET status = $2, payload = $3, reviewed_at = $4
            WHERE preregistration_id = $1 AND status = 'frozen'
            """);
        command.Parameters.AddWithValue(value.PreregistrationId);
        command.Parameters.AddWithValue(value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.ReviewedAt!.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException("阶段 0 预注册不存在或已经复核，不能覆盖。");
        return value;
    }

    public Task<ResearchHypothesis?> GetHypothesisAsync(
        Guid hypothesisId,
        CancellationToken ct = default)
        => ReadHypothesisAsync(hypothesisId, ct);

    public async Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT hypothesis_id FROM research_hypotheses WHERE project_id=$1 ORDER BY updated_at DESC, hypothesis_id;");
        command.Parameters.AddWithValue(projectId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) ids.Add(reader.GetGuid(0));
        var values = new List<ResearchHypothesis>(ids.Count);
        foreach (var id in ids)
            if (await ReadHypothesisAsync(id, ct).ConfigureAwait(false) is { } value) values.Add(value);
        return values;
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
                HypothesisId=hypothesisId, ProjectId=reader.GetGuid(0), Statement=reader.GetString(1),
                Rationale=reader.GetString(2), Status=reader.GetString(3),
                ValidationOutcomeCode=reader.IsDBNull(4)?null:reader.GetString(4),
                ExpectedEffectDirection=reader.IsDBNull(5)?null:reader.GetString(5),
                MinimumEffect=reader.IsDBNull(6)?null:reader.GetDouble(6),
                Applicability=reader.IsDBNull(7)?null:reader.GetString(7), Confidence=reader.GetDouble(8),
                CreatedBy=reader.GetString(9), CreatedAt=reader.GetFieldValue<DateTimeOffset>(10),
                UpdatedAt=reader.GetFieldValue<DateTimeOffset>(11)
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
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new ResearchHypothesisCausalLink { FromVariableCode=reader.GetString(0), ToVariableCode=reader.GetString(1), Mechanism=reader.GetString(2), Direction=reader.IsDBNull(3)?null:reader.GetString(3) });
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisTemporalFeature>> ReadTemporalFeaturesAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT variable_code,feature_code,phase_code,delay_ms,window_ms FROM research_hypothesis_temporal_features WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<ResearchHypothesisTemporalFeature>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new ResearchHypothesisTemporalFeature { VariableCode=reader.GetString(0), FeatureCode=reader.GetString(1), PhaseCode=reader.IsDBNull(2)?null:reader.GetString(2), DelayMilliseconds=reader.IsDBNull(3)?null:reader.GetInt64(3), WindowMilliseconds=reader.IsDBNull(4)?null:reader.GetInt64(4) });
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisInteraction>> ReadInteractionsAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT interaction_id,description FROM research_hypothesis_interactions WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<(Guid Id, string Description)>(); while (await reader.ReadAsync(ct).ConfigureAwait(false)) rows.Add((reader.GetGuid(0),reader.GetString(1)));
        var values = new List<ResearchHypothesisInteraction>();
        foreach (var row in rows)
        {
            await using var variables = _dataSource.CreateCommand("SELECT variable_code FROM research_hypothesis_interaction_variables WHERE interaction_id=@id ORDER BY sequence;");
            variables.Parameters.AddWithValue("id", row.Id); await using var variableReader = await variables.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var codes = new List<string>(); while (await variableReader.ReadAsync(ct).ConfigureAwait(false)) codes.Add(variableReader.GetString(0));
            values.Add(new ResearchHypothesisInteraction { VariableCodes=codes, Description=row.Description });
        }
        return values;
    }

    private async Task<IReadOnlyList<ResearchHypothesisFailureCondition>> ReadFailureConditionsAsync(Guid id, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT condition,observable_signal,required_response FROM research_hypothesis_failure_conditions WHERE hypothesis_id=@id ORDER BY sequence;");
        command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<ResearchHypothesisFailureCondition>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new ResearchHypothesisFailureCondition { Condition=reader.GetString(0), ObservableSignal=reader.GetString(1), RequiredResponse=reader.GetString(2) });
        return values;
    }

    private async Task<IReadOnlyList<EvidenceReference>> ReadHypothesisEvidenceAsync(Guid id, string role, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand("SELECT evidence_id,project_id,kind,reference_id,summary,content_hash,created_at FROM research_hypothesis_evidence WHERE hypothesis_id=@id AND evidence_role=@role ORDER BY created_at,evidence_id;");
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("role", role);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false); var values = new List<EvidenceReference>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(new EvidenceReference { EvidenceId=reader.GetGuid(0), ProjectId=reader.GetGuid(1), Kind=reader.GetString(2), ReferenceId=reader.GetString(3), Summary=reader.GetString(4), ContentHash=reader.GetString(5), CreatedAt=reader.GetFieldValue<DateTimeOffset>(6) });
        return values;
    }

    private static async Task ReplaceHypothesisChildrenAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ResearchHypothesis value, CancellationToken ct)
    {
        foreach (var table in new[] { "research_hypothesis_variables", "research_hypothesis_confounders", "research_hypothesis_causal_links", "research_hypothesis_temporal_features", "research_hypothesis_interactions", "research_hypothesis_failure_conditions", "research_hypothesis_falsification_conditions", "research_hypothesis_evidence" })
        {
            await using var delete = new NpgsqlCommand($"DELETE FROM {table} WHERE hypothesis_id=@id;", connection, transaction);
            delete.Parameters.AddWithValue("id", value.HypothesisId); await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        for (var i=0;i<value.VariableCodes.Count;i++) await InsertSimpleChildAsync(connection,transaction,"research_hypothesis_variables","variable_code",value.HypothesisId,i,value.VariableCodes[i],ct).ConfigureAwait(false);
        for (var i=0;i<value.PossibleConfounders.Count;i++) await InsertSimpleChildAsync(connection,transaction,"research_hypothesis_confounders","description",value.HypothesisId,i,value.PossibleConfounders[i],ct).ConfigureAwait(false);
        for (var i=0;i<value.FalsificationConditions.Count;i++) await InsertSimpleChildAsync(connection,transaction,"research_hypothesis_falsification_conditions","condition",value.HypothesisId,i,value.FalsificationConditions[i],ct).ConfigureAwait(false);
        for (var i=0;i<value.CausalChain.Count;i++)
        {
            var item=value.CausalChain[i]; await using var command=new NpgsqlCommand("INSERT INTO research_hypothesis_causal_links VALUES (@id,@sequence,@from,@to,@mechanism,@direction);",connection,transaction);
            command.Parameters.AddWithValue("id",value.HypothesisId); command.Parameters.AddWithValue("sequence",i); command.Parameters.AddWithValue("from",item.FromVariableCode); command.Parameters.AddWithValue("to",item.ToVariableCode); command.Parameters.AddWithValue("mechanism",item.Mechanism); AddNullable(command,"direction",NpgsqlDbType.Text,item.Direction); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        for (var i=0;i<value.TemporalFeatures.Count;i++)
        {
            var item=value.TemporalFeatures[i]; await using var command=new NpgsqlCommand("INSERT INTO research_hypothesis_temporal_features VALUES (@id,@sequence,@variable,@feature,@phase,@delay,@window);",connection,transaction);
            command.Parameters.AddWithValue("id",value.HypothesisId); command.Parameters.AddWithValue("sequence",i); command.Parameters.AddWithValue("variable",item.VariableCode); command.Parameters.AddWithValue("feature",item.FeatureCode); AddNullable(command,"phase",NpgsqlDbType.Text,item.PhaseCode); AddNullable(command,"delay",NpgsqlDbType.Bigint,item.DelayMilliseconds); AddNullable(command,"window",NpgsqlDbType.Bigint,item.WindowMilliseconds); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        for (var i=0;i<value.Interactions.Count;i++)
        {
            var item=value.Interactions[i]; var interactionId=Guid.CreateVersion7(); await using var command=new NpgsqlCommand("INSERT INTO research_hypothesis_interactions VALUES (@interaction_id,@id,@sequence,@description);",connection,transaction);
            command.Parameters.AddWithValue("interaction_id",interactionId); command.Parameters.AddWithValue("id",value.HypothesisId); command.Parameters.AddWithValue("sequence",i); command.Parameters.AddWithValue("description",item.Description); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            for(var j=0;j<item.VariableCodes.Count;j++){await using var variable=new NpgsqlCommand("INSERT INTO research_hypothesis_interaction_variables VALUES (@interaction_id,@sequence,@code);",connection,transaction);variable.Parameters.AddWithValue("interaction_id",interactionId);variable.Parameters.AddWithValue("sequence",j);variable.Parameters.AddWithValue("code",item.VariableCodes[j]);await variable.ExecuteNonQueryAsync(ct).ConfigureAwait(false);}
        }
        for(var i=0;i<value.FailureConditions.Count;i++){var item=value.FailureConditions[i];await using var command=new NpgsqlCommand("INSERT INTO research_hypothesis_failure_conditions VALUES (@failure_id,@id,@sequence,@condition,@signal,@response);",connection,transaction);command.Parameters.AddWithValue("failure_id",Guid.CreateVersion7());command.Parameters.AddWithValue("id",value.HypothesisId);command.Parameters.AddWithValue("sequence",i);command.Parameters.AddWithValue("condition",item.Condition);command.Parameters.AddWithValue("signal",item.ObservableSignal);command.Parameters.AddWithValue("response",item.RequiredResponse);await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);}
        foreach(var pair in new[]{("supporting",value.SupportingEvidence),("opposing",value.OpposingEvidence),("validation",value.ValidationEvidence)})
            foreach(var item in pair.Item2){await using var command=new NpgsqlCommand("INSERT INTO research_hypothesis_evidence VALUES (@id,@evidence_id,@role,@project_id,@kind,@reference,@summary,@hash,@created_at);",connection,transaction);command.Parameters.AddWithValue("id",value.HypothesisId);command.Parameters.AddWithValue("evidence_id",item.EvidenceId);command.Parameters.AddWithValue("role",pair.Item1);command.Parameters.AddWithValue("project_id",item.ProjectId);command.Parameters.AddWithValue("kind",item.Kind);command.Parameters.AddWithValue("reference",item.ReferenceId);command.Parameters.AddWithValue("summary",item.Summary);command.Parameters.AddWithValue("hash",item.ContentHash);command.Parameters.AddWithValue("created_at",item.CreatedAt);await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);}
    }

    private static async Task InsertSimpleChildAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,string table,string column,Guid id,int sequence,string value,CancellationToken ct)
    {
        var allowed=(table,column) is ("research_hypothesis_variables","variable_code") or ("research_hypothesis_confounders","description") or ("research_hypothesis_falsification_conditions","condition");
        if(!allowed) throw new InvalidOperationException("不允许写入未注册的假设子表。");
        await using var command=new NpgsqlCommand($"INSERT INTO {table}(hypothesis_id,sequence,{column}) VALUES (@id,@sequence,@value);",connection,transaction);command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("sequence",sequence);command.Parameters.AddWithValue("value",value);await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

    public Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
        Guid projectId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
        => ListPageAsync<ResearchAuditEntry>(
            """
            SELECT payload
            FROM process_research_audit
            WHERE project_id = $1
              AND ($2::timestamptz IS NULL OR (created_at, entry_id) < ($2, $3))
            ORDER BY created_at DESC, entry_id DESC
            LIMIT $4
            """,
            projectId,
            cursor,
            limit,
            static value => value.CreatedAt,
            static value => value.EntryId,
            ct);

    private async Task<ResearchPage<T>> ListPageAsync<T>(
        string sql,
        Guid projectId,
        string? cursor,
        int limit,
        Func<T, DateTimeOffset> timestamp,
        Func<T, Guid> id,
        CancellationToken ct)
    {
        DateTimeOffset? beforeTime = null;
        Guid? beforeId = null;
        if (cursor is not null)
        {
            if (!ResearchPageCursor.TryDecode(cursor, out var decodedTime, out var decodedId))
                throw new ProcessResearchRuleException("分页游标无效或已经损坏。");
            beforeTime = decodedTime;
            beforeId = decodedId;
        }
        limit = Math.Clamp(limit, 1, 200);
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = (object?)beforeTime ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Uuid,
            Value = (object?)beforeId ?? DBNull.Value
        });
        command.Parameters.AddWithValue(limit + 1);
        var values = new List<T>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        var hasMore = values.Count > limit;
        if (hasMore) values.RemoveAt(values.Count - 1);
        var last = hasMore ? values[^1] : default;
        return new ResearchPage<T>
        {
            Items = values,
            NextCursor = last is null ? null : ResearchPageCursor.Encode(timestamp(last), id(last))
        };
    }

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

    private static async Task SaveExperimentCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResearchExperiment value,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO research_experiments
              (experiment_id, project_id, status, revision, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (experiment_id) DO UPDATE SET
              status = EXCLUDED.status,
              revision = EXCLUDED.revision,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            WHERE research_experiments.revision = EXCLUDED.revision - 1
            """;
        command.Parameters.AddWithValue(value.ExperimentId);
        command.Parameters.AddWithValue(value.ProjectId);
        command.Parameters.AddWithValue(value.Status);
        command.Parameters.AddWithValue(value.Revision);
        AddJson(command, value);
        command.Parameters.AddWithValue(value.CreatedAt);
        command.Parameters.AddWithValue(value.UpdatedAt);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new ProcessResearchRuleException(
                "实验已被其他人修改，请刷新后重试。");

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
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResearchAuditEntry audit,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO process_research_audit
              (entry_id, project_id, resource_type, resource_id, action, payload, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            """;
        command.Parameters.AddWithValue(audit.EntryId);
        command.Parameters.AddWithValue(audit.ProjectId);
        command.Parameters.AddWithValue(audit.ResourceType);
        command.Parameters.AddWithValue(audit.ResourceId);
        command.Parameters.AddWithValue(audit.Action);
        AddJson(command, audit);
        command.Parameters.AddWithValue(audit.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddJson<T>(NpgsqlCommand command, T value)
        => command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(value, JsonOptions));

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法解析 {typeof(T).Name}。");

    private static JsonSerializerOptions CreateJsonOptions()
        => new(JsonSerializerDefaults.Web);

}
