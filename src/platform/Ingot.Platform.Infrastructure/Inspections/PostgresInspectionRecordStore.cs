using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Inspections;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class PostgresInspectionRecordStore : IInspectionRecordStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresInspectionRecordStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS inspection_records (
                  record_id           UUID PRIMARY KEY,
                  workpiece_id        TEXT NOT NULL,
                  operation_run_id    TEXT NOT NULL,
                  definition_code     TEXT NOT NULL,
                  definition_version  INTEGER NOT NULL,
                  measured_at         TIMESTAMPTZ NOT NULL,
                  recorded_at         TIMESTAMPTZ NOT NULL,
                  ingested_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
                  outcome             TEXT NOT NULL,
                  submitted_by        TEXT NOT NULL,
                  submitter_verified  BOOLEAN NOT NULL,
                  instrument          JSONB,
                  measurements        JSONB NOT NULL DEFAULT '[]'::jsonb,
                  attachments            JSONB NOT NULL DEFAULT '[]'::jsonb,
                  notes               TEXT,
                  supersedes_record_id UUID,
                  correction_reason   TEXT,
                  payload_hash        TEXT NOT NULL,
                  CHECK (definition_version > 0),
                  CHECK (outcome IN ('PASS', 'FAIL', 'INCONCLUSIVE'))
                );
                CREATE INDEX IF NOT EXISTS idx_inspection_records_workpiece_time
                  ON inspection_records(workpiece_id, measured_at DESC);
                CREATE INDEX IF NOT EXISTS idx_inspection_records_operation_time
                  ON inspection_records(operation_run_id, measured_at DESC);
                CREATE INDEX IF NOT EXISTS idx_inspection_records_definition_time
                  ON inspection_records(definition_code, measured_at DESC);
                CREATE INDEX IF NOT EXISTS idx_inspection_records_outcome_time
                  ON inspection_records(outcome, measured_at DESC);
                ALTER TABLE inspection_records ADD COLUMN IF NOT EXISTS supersedes_record_id UUID;
                ALTER TABLE inspection_records ADD COLUMN IF NOT EXISTS correction_reason TEXT;
                CREATE UNIQUE INDEX IF NOT EXISTS idx_inspection_records_one_correction
                  ON inspection_records(supersedes_record_id) WHERE supersedes_record_id IS NOT NULL;

                CREATE TABLE IF NOT EXISTS inspection_scopes (
                  scope_id TEXT PRIMARY KEY,
                  scope_type TEXT NOT NULL,
                  subject_id TEXT NOT NULL,
                  from_at TIMESTAMPTZ NOT NULL,
                  to_at TIMESTAMPTZ NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  CHECK (scope_type IN ('analysis-window', 'production-run', 'material-lot')),
                  CHECK (to_at > from_at)
                );
                CREATE INDEX IF NOT EXISTS idx_inspection_scopes_subject_time
                  ON inspection_scopes(subject_id, to_at DESC);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

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
              record_id, workpiece_id, operation_run_id, definition_code, definition_version,
              measured_at, recorded_at, outcome, submitted_by, submitter_verified, instrument,
              measurements, attachments, notes, supersedes_record_id, correction_reason, payload_hash)
            VALUES (
              @record_id, @workpiece_id, @operation_run_id, @definition_code, @definition_version,
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
        AddEquality(command, predicates, "workpiece_id", "workpiece_id", query.WorkpieceId);
        AddEquality(command, predicates, "operation_run_id", "operation_run_id", query.OperationRunId);
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
        AddEquality(countCommand, predicates, "workpiece_id", "workpiece_id", query.WorkpieceId);
        AddEquality(countCommand, predicates, "operation_run_id", "operation_run_id", query.OperationRunId);
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

    public async Task<IReadOnlyList<InspectionRecord>> QueryAllByOperationRunIdsAsync(
        IReadOnlyCollection<string> operationRunIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operationRunIds);
        var normalizedIds = operationRunIds
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
             WHERE operation_run_id = ANY(@operation_run_ids)
             ORDER BY measured_at DESC, record_id DESC;
             """);
        command.Parameters.AddWithValue("operation_run_ids", normalizedIds);
        var records = new List<InspectionRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            records.Add(Read(reader).Record);
        return records;
    }

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
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
        command.Parameters.AddWithValue("workpiece_id", request.WorkpieceId);
        command.Parameters.AddWithValue("operation_run_id", request.OperationRunId);
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
                WorkpieceId = reader.GetString(1),
                OperationRunId = reader.GetString(2),
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
        SELECT record_id, workpiece_id, operation_run_id, definition_code, definition_version,
               measured_at, recorded_at, ingested_at, outcome, submitted_by, submitter_verified,
               instrument::text, measurements::text, attachments::text, notes,
               supersedes_record_id, correction_reason, payload_hash
        FROM inspection_records
        """;
}
