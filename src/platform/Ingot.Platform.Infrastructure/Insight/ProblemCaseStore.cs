using System.Text.Json;
using Ingot.Contracts.Insight;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Insight;

public interface IProblemCaseStore
{
    Task<IReadOnlyList<ProblemCase>> ListAsync(string? status, CancellationToken ct = default);
    Task<ProblemCase?> GetAsync(Guid caseId, CancellationToken ct = default);
    Task<ProblemCase> UpsertAsync(ProblemCase value, CancellationToken ct = default);
    Task<ProblemCase?> SetRatifiedAsync(Guid caseId, bool ratified, string by, CancellationToken ct = default);
    Task UpdateLevelAsync(Guid caseId, string level, CancellationToken ct = default);
    Task SaveEvaluationAsync(LevelEvaluation evaluation, CancellationToken ct = default);
    Task<IReadOnlyList<LevelEvaluation>> ListEvaluationsAsync(Guid caseId, int limit, CancellationToken ct = default);
}

/// <summary>问题档案与定级评估的 PostgreSQL 存储。schema 由迁移 0002 保证，本类不做 DDL。</summary>
public sealed class PostgresProblemCaseStore : IProblemCaseStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProblemCaseStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    private const string SelectColumns = """
        case_id, title, description, status, subject_type, subject_id,
        context_filter::text, comparison_key, window_from, window_to, target_metric,
        current_level, feature_set_ratified, ratified_by, ratified_at, owner, created_at, updated_at
        """;

    public async Task<IReadOnlyList<ProblemCase>> ListAsync(string? status, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM problem_cases " +
            (string.IsNullOrWhiteSpace(status) ? "" : "WHERE status = @status ") +
            "ORDER BY updated_at DESC;");
        if (!string.IsNullOrWhiteSpace(status))
            command.Parameters.AddWithValue("status", status);
        var result = new List<ProblemCase>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadCase(reader));
        return result;
    }

    public async Task<ProblemCase?> GetAsync(Guid caseId, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            $"SELECT {SelectColumns} FROM problem_cases WHERE case_id = @case_id;");
        command.Parameters.AddWithValue("case_id", caseId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadCase(reader) : null;
    }

    public async Task<ProblemCase> UpsertAsync(ProblemCase value, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            INSERT INTO problem_cases(
              case_id, title, description, status, subject_type, subject_id, context_filter,
              comparison_key, window_from, window_to, target_metric, current_level,
              feature_set_ratified, ratified_by, ratified_at, owner, created_at, updated_at)
            VALUES (
              @case_id, @title, @description, @status, @subject_type, @subject_id, @context_filter,
              @comparison_key, @window_from, @window_to, @target_metric, @current_level,
              @feature_set_ratified, @ratified_by, @ratified_at, @owner, @created_at, @updated_at)
            ON CONFLICT (case_id) DO UPDATE SET
              title = EXCLUDED.title, description = EXCLUDED.description, status = EXCLUDED.status,
              subject_type = EXCLUDED.subject_type, subject_id = EXCLUDED.subject_id,
              context_filter = EXCLUDED.context_filter, comparison_key = EXCLUDED.comparison_key,
              window_from = EXCLUDED.window_from, window_to = EXCLUDED.window_to,
              target_metric = EXCLUDED.target_metric, owner = EXCLUDED.owner,
              updated_at = EXCLUDED.updated_at;
            """);
        BindCase(command, value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<ProblemCase?> SetRatifiedAsync(Guid caseId, bool ratified, string by, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            UPDATE problem_cases
            SET feature_set_ratified = @ratified,
                ratified_by = CASE WHEN @ratified THEN @by ELSE NULL END,
                ratified_at = CASE WHEN @ratified THEN now() ELSE NULL END,
                updated_at = now()
            WHERE case_id = @case_id;
            """);
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("ratified", ratified);
        command.Parameters.AddWithValue("by", by);
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected == 0 ? null : await GetAsync(caseId, ct).ConfigureAwait(false);
    }

    public async Task UpdateLevelAsync(Guid caseId, string level, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "UPDATE problem_cases SET current_level = @level, updated_at = now() WHERE case_id = @case_id;");
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("level", level);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveEvaluationAsync(LevelEvaluation evaluation, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            INSERT INTO case_level_evaluations(evaluation_id, case_id, evaluated_at, level, gates, window_days)
            VALUES (@evaluation_id, @case_id, @evaluated_at, @level, @gates, @window_days);
            """);
        command.Parameters.AddWithValue("evaluation_id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("case_id", evaluation.CaseId);
        command.Parameters.AddWithValue("evaluated_at", evaluation.EvaluatedAt.UtcDateTime);
        command.Parameters.AddWithValue("level", evaluation.Level);
        command.Parameters.Add(new NpgsqlParameter("gates", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(evaluation.Gates, JsonOptions)
        });
        command.Parameters.AddWithValue("window_days", evaluation.WindowDays);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LevelEvaluation>> ListEvaluationsAsync(Guid caseId, int limit, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            SELECT case_id, evaluated_at, level, gates::text, window_days
            FROM case_level_evaluations
            WHERE case_id = @case_id
            ORDER BY evaluated_at DESC
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("case_id", caseId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 200));
        var result = new List<LevelEvaluation>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var gates = JsonSerializer.Deserialize<List<LevelGate>>(reader.GetString(3), JsonOptions) ?? [];
            result.Add(new LevelEvaluation
            {
                CaseId = reader.GetGuid(0),
                EvaluatedAt = new DateTimeOffset(reader.GetDateTime(1).ToUniversalTime()),
                Level = reader.GetString(2),
                Gates = gates,
                WindowDays = reader.GetInt32(4)
            });
        }
        return result;
    }

    private static ProblemCase ReadCase(NpgsqlDataReader reader)
    {
        var filter = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6), JsonOptions)
                     ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return new ProblemCase
        {
            CaseId = reader.GetGuid(0),
            Title = reader.GetString(1),
            Description = reader.GetString(2),
            Status = reader.GetString(3),
            Scope = new CaseScope
            {
                SubjectType = reader.IsDBNull(4) ? null : reader.GetString(4),
                SubjectId = reader.IsDBNull(5) ? null : reader.GetString(5),
                ContextFilter = filter,
                ComparisonKey = reader.IsDBNull(7) ? null : reader.GetString(7),
                WindowFrom = reader.IsDBNull(8) ? null : new DateTimeOffset(reader.GetDateTime(8).ToUniversalTime()),
                WindowTo = reader.IsDBNull(9) ? null : new DateTimeOffset(reader.GetDateTime(9).ToUniversalTime())
            },
            TargetMetric = reader.GetString(10),
            CurrentLevel = reader.GetString(11),
            FeatureSetRatified = reader.GetBoolean(12),
            RatifiedBy = reader.IsDBNull(13) ? null : reader.GetString(13),
            RatifiedAt = reader.IsDBNull(14) ? null : new DateTimeOffset(reader.GetDateTime(14).ToUniversalTime()),
            Owner = reader.IsDBNull(15) ? null : reader.GetString(15),
            CreatedAt = new DateTimeOffset(reader.GetDateTime(16).ToUniversalTime()),
            UpdatedAt = new DateTimeOffset(reader.GetDateTime(17).ToUniversalTime())
        };
    }

    private static void BindCase(NpgsqlCommand command, ProblemCase value)
    {
        command.Parameters.AddWithValue("case_id", value.CaseId);
        command.Parameters.AddWithValue("title", value.Title);
        command.Parameters.AddWithValue("description", value.Description);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("subject_type", (object?)value.Scope.SubjectType ?? DBNull.Value);
        command.Parameters.AddWithValue("subject_id", (object?)value.Scope.SubjectId ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("context_filter", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value.Scope.ContextFilter, JsonOptions)
        });
        command.Parameters.AddWithValue("comparison_key", (object?)value.Scope.ComparisonKey ?? DBNull.Value);
        command.Parameters.AddWithValue("window_from", (object?)value.Scope.WindowFrom?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("window_to", (object?)value.Scope.WindowTo?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("target_metric", value.TargetMetric);
        command.Parameters.AddWithValue("current_level", value.CurrentLevel);
        command.Parameters.AddWithValue("feature_set_ratified", value.FeatureSetRatified);
        command.Parameters.AddWithValue("ratified_by", (object?)value.RatifiedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("ratified_at", (object?)value.RatifiedAt?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("owner", (object?)value.Owner ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt.UtcDateTime);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
