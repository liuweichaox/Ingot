using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Inspections;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Inspections.Infrastructure;

public sealed class PostgresInspectionRecordStore : IInspectionRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresInspectionRecordStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<StoreInspectionRecordResult> CreateAsync(
        CreateInspectionRecordRequest request,
        bool submitterVerified,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await InitializeAsync(ct).ConfigureAwait(false);
        var payloadHash = ComputePayloadHash(request);

        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO inspection_records(
              record_id, output_item_id, execution_id, definition_code, definition_version,
              measured_at, recorded_at, outcome, submitted_by, submitter_verified, instrument,
              measurements, attachments, notes, supersedes_record_id, correction_reason, payload_hash)
            VALUES (
              @record_id, @output_item_id, @execution_id, @definition_code, @definition_version,
              @measured_at, @recorded_at, @outcome, @submitted_by, @submitter_verified, @instrument,
              @measurements, @attachments, @notes, @supersedes_record_id, @correction_reason, @payload_hash)
            ON CONFLICT (record_id) DO NOTHING
            RETURNING record_id;
            """);
        AddRequestParameters(command, request, submitterVerified, payloadHash);
        var created = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
        var stored = await GetWithHashAsync(request.RecordId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("检测记录写入后无法读取。");

        return new StoreInspectionRecordResult
        {
            Record = stored.Record,
            Created = created,
            PayloadConflict = !created &&
                              !string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal)
        };
    }

    public async Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default)
        => (await GetWithHashAsync(recordId, ct).ConfigureAwait(false))?.Record;

    public async Task<InspectionRecord?> GetCorrectionForAsync(Guid recordId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"{SelectColumns} WHERE supersedes_record_id = @record_id ORDER BY ingested_at DESC LIMIT 1;");
        command.Parameters.AddWithValue("record_id", recordId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader).Record : null;
    }

    public async Task<IReadOnlyList<InspectionScope>> ListScopesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT payload::text FROM inspection_scopes ORDER BY to_at DESC, scope_id;");
        var values = new List<InspectionScope>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(JsonSerializer.Deserialize<InspectionScope>(reader.GetString(0), JsonOptions)!);
        return values;
    }

    public async Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "SELECT payload::text FROM inspection_scopes WHERE scope_id = @scope_id;");
        command.Parameters.AddWithValue("scope_id", scopeId);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null or DBNull
            ? null
            : JsonSerializer.Deserialize<InspectionScope>((string)payload, JsonOptions);
    }

    public async Task<InspectionScope> UpsertScopeAsync(InspectionScope scope, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO inspection_scopes(scope_id, scope_type, subject_id, from_at, to_at, payload, updated_at)
            VALUES (@scope_id, @scope_type, @subject_id, @from_at, @to_at, @payload, now())
            ON CONFLICT (scope_id) DO UPDATE SET
              scope_type = EXCLUDED.scope_type,
              subject_id = EXCLUDED.subject_id,
              from_at = EXCLUDED.from_at,
              to_at = EXCLUDED.to_at,
              payload = EXCLUDED.payload,
              updated_at = now();
            """);
        command.Parameters.AddWithValue("scope_id", scope.ScopeId);
        command.Parameters.AddWithValue("scope_type", scope.ScopeType);
        command.Parameters.AddWithValue("subject_id", scope.SubjectId);
        command.Parameters.AddWithValue("from_at", scope.From.UtcDateTime);
        command.Parameters.AddWithValue("to_at", scope.To.UtcDateTime);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(scope, JsonOptions));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return scope;
    }

    public async Task<bool> DeleteScopeAsync(string scopeId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "DELETE FROM inspection_scopes WHERE scope_id = @scope_id;");
        command.Parameters.AddWithValue("scope_id", scopeId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);

        await using var command = _dataSource.CreateCommand();
        var predicates = new List<string>();
        AddEquality(command, predicates, "output_item_id", "output_item_id", query.OutputItemId);
        AddEquality(command, predicates, "execution_id", "execution_id", query.ExecutionId);
        AddEquality(command, predicates, "definition_code", "definition_code", query.DefinitionCode);
        AddEquality(command, predicates, "outcome", "outcome", query.Outcome?.ToUpperInvariant());
        if (query.From.HasValue)
        {
            predicates.Add("measured_at >= @from");
            command.Parameters.AddWithValue("from", query.From.Value.UtcDateTime);
        }
        if (query.To.HasValue)
        {
            predicates.Add("measured_at <= @to");
            command.Parameters.AddWithValue("to", query.To.Value.UtcDateTime);
        }

        var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
        command.CommandText = $"""
                              {SelectColumns}
                              {where}
                              ORDER BY measured_at DESC, record_id DESC
                              LIMIT @limit OFFSET @offset;
                              """;
        command.Parameters.AddWithValue("limit", query.Limit);
        command.Parameters.AddWithValue("offset", query.Offset);
        var records = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            records.Add(Read(reader).Record);
        return records;
    }

    public async Task<InspectionRecordPage> QueryPageAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var countCommand = _dataSource.CreateCommand();
        var predicates = new List<string>();
        AddEquality(countCommand, predicates, "output_item_id", "output_item_id", query.OutputItemId);
        AddEquality(countCommand, predicates, "execution_id", "execution_id", query.ExecutionId);
        AddEquality(countCommand, predicates, "definition_code", "definition_code", query.DefinitionCode);
        AddEquality(countCommand, predicates, "outcome", "outcome", query.Outcome?.ToUpperInvariant());
        if (query.From.HasValue)
        {
            predicates.Add("measured_at >= @from");
            countCommand.Parameters.AddWithValue("from", query.From.Value.UtcDateTime);
        }
        if (query.To.HasValue)
        {
            predicates.Add("measured_at <= @to");
            countCommand.Parameters.AddWithValue("to", query.To.Value.UtcDateTime);
        }
        var where = predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}";
        countCommand.CommandText = $"SELECT COUNT(*) FROM inspection_records {where};";
        var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));
        var data = await QueryAsync(query, ct).ConfigureAwait(false);
        return new InspectionRecordPage
        {
            Data = data,
            Total = total,
            Offset = query.Offset,
            Limit = query.Limit
        };
    }

    public async Task<IReadOnlyList<InspectionRecord>> QueryAllByExecutionIdsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executionIds);
        var normalizedIds = executionIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedIds.Length == 0)
            return [];

        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"""
             {SelectColumns}
             WHERE execution_id = ANY(@execution_ids)
             ORDER BY measured_at DESC, record_id DESC;
             """);
        command.Parameters.AddWithValue("execution_ids", normalizedIds);
        var records = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            records.Add(Read(reader).Record);
        return records;
    }


    private async Task<StoredInspectionRecord?> GetWithHashAsync(
        Guid recordId,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"{SelectColumns} WHERE record_id = @record_id;");
        command.Parameters.AddWithValue("record_id", recordId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static void AddRequestParameters(
        NpgsqlCommand command,
        CreateInspectionRecordRequest request,
        bool submitterVerified,
        string payloadHash)
    {
        command.Parameters.AddWithValue("record_id", request.RecordId);
        command.Parameters.AddWithValue(
            "output_item_id",
            NpgsqlDbType.Text,
            (object?)request.OutputItemId ?? DBNull.Value);
        command.Parameters.AddWithValue("execution_id", request.ExecutionId);
        command.Parameters.AddWithValue("definition_code", request.DefinitionCode);
        command.Parameters.AddWithValue("definition_version", request.DefinitionVersion);
        command.Parameters.AddWithValue("measured_at", request.MeasuredAt.UtcDateTime);
        command.Parameters.AddWithValue("recorded_at", request.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("outcome", request.Outcome);
        command.Parameters.AddWithValue("submitted_by", request.SubmittedBy);
        command.Parameters.AddWithValue("submitter_verified", submitterVerified);
        command.Parameters.AddWithValue(
            "instrument",
            NpgsqlDbType.Jsonb,
            request.Instrument is null
                ? DBNull.Value
                : JsonSerializer.Serialize(request.Instrument, JsonOptions));
        command.Parameters.AddWithValue(
            "measurements",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(request.Measurements, JsonOptions));
        command.Parameters.AddWithValue(
            "attachments",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(request.Attachments, JsonOptions));
        command.Parameters.AddWithValue("notes", (object?)request.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("supersedes_record_id", (object?)request.SupersedesRecordId ?? DBNull.Value);
        command.Parameters.AddWithValue("correction_reason", (object?)request.CorrectionReason ?? DBNull.Value);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
    }

    private static StoredInspectionRecord Read(NpgsqlDataReader reader)
    {
        var instrument = reader.IsDBNull(11)
            ? null
            : JsonSerializer.Deserialize<InspectionInstrumentRef>(reader.GetString(11), JsonOptions);
        var measurements = JsonSerializer.Deserialize<InspectionCharacteristicResult[]>(
                               reader.GetString(12), JsonOptions) ?? [];
        var attachments = JsonSerializer.Deserialize<InspectionAttachment[]>(
                           reader.GetString(13), JsonOptions) ?? [];
        return new StoredInspectionRecord(
            new InspectionRecord
            {
                RecordId = reader.GetGuid(0),
                OutputItemId = reader.IsDBNull(1) ? null : reader.GetString(1),
                ExecutionId = reader.GetString(2),
                DefinitionCode = reader.GetString(3),
                DefinitionVersion = reader.GetInt32(4),
                MeasuredAt = ToUtc(reader.GetDateTime(5)),
                RecordedAt = ToUtc(reader.GetDateTime(6)),
                IngestedAt = ToUtc(reader.GetDateTime(7)),
                Outcome = reader.GetString(8),
                SubmittedBy = reader.GetString(9),
                SubmitterVerified = reader.GetBoolean(10),
                Instrument = instrument,
                Measurements = measurements,
                Attachments = attachments,
                Notes = reader.IsDBNull(14) ? null : reader.GetString(14),
                SupersedesRecordId = reader.IsDBNull(15) ? null : reader.GetGuid(15),
                CorrectionReason = reader.IsDBNull(16) ? null : reader.GetString(16)
            },
            reader.GetString(17));
    }

    private static DateTimeOffset ToUtc(DateTime value)
        => new(value.ToUniversalTime());

    private static string ComputePayloadHash(CreateInspectionRecordRequest request)
        => Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)));

    private static void AddEquality(
        NpgsqlCommand command,
        ICollection<string> predicates,
        string column,
        string parameter,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        predicates.Add($"{column} = @{parameter}");
        command.Parameters.AddWithValue(parameter, value.Trim());
    }

    private sealed record StoredInspectionRecord(InspectionRecord Record, string PayloadHash);

    private const string SelectColumns = """
        SELECT record_id, output_item_id, execution_id, definition_code, definition_version,
               measured_at, recorded_at, ingested_at, outcome, submitted_by, submitter_verified,
               instrument::text, measurements::text, attachments::text, notes,
               supersedes_record_id, correction_reason, payload_hash
        FROM inspection_records
        """;
}
