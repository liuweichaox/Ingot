using System.Text.Json;
using Ingot.Contracts.Agents;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Insight;

public interface IGoldenQuestionStore
{
    Task<IReadOnlyList<GoldenQuestionCase>> ListAsync(string? status, CancellationToken ct = default);
    Task<GoldenQuestionCase?> GetAsync(Guid caseId, int version, CancellationToken ct = default);
    Task<GoldenQuestionCase> SaveAsync(GoldenQuestionCase value, CancellationToken ct = default);
    Task SaveEvaluationAsync(GoldenQuestionEvaluation value, CancellationToken ct = default);
    Task<IReadOnlyList<GoldenQuestionEvaluation>> ListEvaluationsAsync(
        Guid? caseId,
        int limit,
        CancellationToken ct = default);
}

public sealed class PostgresGoldenQuestionStore : IGoldenQuestionStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresGoldenQuestionStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<IReadOnlyList<GoldenQuestionCase>> ListAsync(
        string? status,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT payload::text FROM golden_question_cases
            WHERE (@status IS NULL OR status = @status)
            ORDER BY updated_at DESC, case_id, version DESC
            LIMIT 500;
            """);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Text)
        {
            Value = (object?)NullIfBlank(status) ?? DBNull.Value
        });
        var result = new List<GoldenQuestionCase>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var value = JsonSerializer.Deserialize<GoldenQuestionCase>(reader.GetString(0), JsonOptions);
            if (value is not null) result.Add(value);
        }
        return result;
    }

    public async Task<GoldenQuestionCase?> GetAsync(Guid caseId, int version, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT payload::text FROM golden_question_cases
            WHERE case_id = @case_id AND version = @version;
            """);
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("version", version);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : JsonSerializer.Deserialize<GoldenQuestionCase>(json, JsonOptions);
    }

    public async Task<GoldenQuestionCase> SaveAsync(GoldenQuestionCase value, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            INSERT INTO golden_question_cases(
              case_id, version, status, question, payload, created_at, updated_at)
            VALUES (@case_id, @version, @status, @question, @payload, @created_at, @updated_at)
            ON CONFLICT (case_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              question = EXCLUDED.question,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            WHERE golden_question_cases.status = 'draft';
            """);
        command.Parameters.AddWithValue("case_id", value.CaseId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("question", value.Question);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions)
        });
        command.Parameters.AddWithValue("created_at", value.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("已审核的黄金问题版本不可修改；请创建新版本。");
        return value;
    }

    public async Task SaveEvaluationAsync(GoldenQuestionEvaluation value, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            INSERT INTO golden_question_evaluations(
              evaluation_id, case_id, case_version, agent_run_id, passed, payload, evaluated_at)
            VALUES (@evaluation_id, @case_id, @case_version, @agent_run_id, @passed, @payload, @evaluated_at);
            """);
        command.Parameters.AddWithValue("evaluation_id", value.EvaluationId);
        command.Parameters.AddWithValue("case_id", value.CaseId);
        command.Parameters.AddWithValue("case_version", value.CaseVersion);
        command.Parameters.AddWithValue("agent_run_id", value.AgentRunId);
        command.Parameters.AddWithValue("passed", value.Passed);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions)
        });
        command.Parameters.AddWithValue("evaluated_at", value.EvaluatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GoldenQuestionEvaluation>> ListEvaluationsAsync(
        Guid? caseId,
        int limit,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT payload::text FROM golden_question_evaluations
            WHERE (@case_id IS NULL OR case_id = @case_id)
            ORDER BY evaluated_at DESC
            LIMIT @limit;
            """);
        command.Parameters.Add(new NpgsqlParameter("case_id", NpgsqlDbType.Uuid)
        {
            Value = (object?)caseId ?? DBNull.Value
        });
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        var result = new List<GoldenQuestionEvaluation>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var value = JsonSerializer.Deserialize<GoldenQuestionEvaluation>(reader.GetString(0), JsonOptions);
            if (value is not null) result.Add(value);
        }
        return result;
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
