using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed class PostgresProcessResearchStore : IProcessResearchStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

    public Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(CancellationToken ct = default)
        => ListAsync<ResearchProject>(
            """
            SELECT payload
            FROM process_research_projects
            ORDER BY updated_at DESC, project_id
            """,
            null,
            ct);

    public async Task<ResearchProject> SaveProjectAsync(
        ResearchProject value,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
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
            """);
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

    public Task<ResearchHypothesis> SaveHypothesisAsync(
        ResearchHypothesis value,
        CancellationToken ct = default)
        => SaveChildAsync(
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
            ct);

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

    public Task<ResearchExperiment> SaveExperimentAsync(
        ResearchExperiment value,
        CancellationToken ct = default)
        => SaveChildAsync(
            """
            INSERT INTO research_experiments
              (experiment_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (experiment_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """,
            value.ExperimentId,
            value.ProjectId,
            value.Status,
            value,
            value.CreatedAt,
            value.UpdatedAt,
            ct);

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

    public Task<ResearchProcessWindow> SaveProcessWindowAsync(
        ResearchProcessWindow value,
        CancellationToken ct = default)
        => SaveChildAsync(
            """
            INSERT INTO research_process_windows
              (window_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (window_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """,
            value.WindowId,
            value.ProjectId,
            value.Status,
            value,
            value.CreatedAt,
            value.UpdatedAt,
            ct);

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

    public Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        ResearchKnowledgeClaim value,
        CancellationToken ct = default)
        => SaveChildAsync(
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

    private async Task<T> SaveChildAsync<T>(
        string sql,
        Guid id,
        Guid projectId,
        string status,
        T value,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(projectId);
        command.Parameters.AddWithValue(status);
        AddJson(command, value);
        command.Parameters.AddWithValue(createdAt);
        command.Parameters.AddWithValue(updatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    private static void AddJson<T>(NpgsqlCommand command, T value)
        => command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(value, JsonOptions));

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法解析 {typeof(T).Name}。");

    public async ValueTask DisposeAsync()
        => await _dataSource.DisposeAsync().ConfigureAwait(false);
}
