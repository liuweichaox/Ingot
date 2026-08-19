using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Inspections;
using Npgsql;

namespace Ingot.Platform.Inspections.Infrastructure;

public sealed class PostgresInspectionReviewStore : IInspectionReviewStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresInspectionReviewStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<StoreInspectionReviewResult> CreateAsync(
        CreateInspectionReviewRequest request,
        string executionId,
        string reviewedBy,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var normalizedDecision = request.Decision.Trim().ToUpperInvariant();
        var normalizedNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var payloadHash = ComputeHash(request with { Decision = normalizedDecision, Notes = normalizedNotes }, executionId, reviewedBy);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO inspection_reviews(
              review_id, inspection_record_id, execution_id, decision, reviewed_by, notes, payload_hash)
            VALUES (@review_id, @inspection_record_id, @execution_id, @decision, @reviewed_by, @notes, @payload_hash)
            ON CONFLICT (review_id) DO NOTHING
            RETURNING review_id;
            """);
        command.Parameters.AddWithValue("review_id", request.ReviewId);
        command.Parameters.AddWithValue("inspection_record_id", request.InspectionRecordId);
        command.Parameters.AddWithValue("execution_id", executionId);
        command.Parameters.AddWithValue("decision", normalizedDecision);
        command.Parameters.AddWithValue("reviewed_by", reviewedBy);
        command.Parameters.AddWithValue("notes", (object?)normalizedNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
        var created = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
        var stored = await GetWithHashAsync(request.ReviewId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("复核记录写入后无法读取。");
        if (created)
        {
            await LogAccessAsync(
                request.InspectionRecordId,
                null,
                "inspection.reviewed",
                reviewedBy,
                normalizedDecision,
                ct).ConfigureAwait(false);
        }
        return new StoreInspectionReviewResult
        {
            Review = stored.Review,
            Created = created,
            PayloadConflict = !created && !string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal)
        };
    }

    public async Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default)
        => (await GetWithHashAsync(reviewId, ct).ConfigureAwait(false))?.Review;

    public async Task<IReadOnlyList<InspectionReview>> QueryAsync(
        Guid? inspectionRecordId,
        string? executionId,
        int limit,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand();
        var predicates = new List<string>();
        if (inspectionRecordId.HasValue)
        {
            predicates.Add("inspection_record_id = @inspection_record_id");
            command.Parameters.AddWithValue("inspection_record_id", inspectionRecordId.Value);
        }
        if (!string.IsNullOrWhiteSpace(executionId))
        {
            predicates.Add("execution_id = @execution_id");
            command.Parameters.AddWithValue("execution_id", executionId.Trim());
        }
        command.CommandText = $"""
                               {SelectColumns}
                               {(predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}")}
                               ORDER BY reviewed_at DESC, review_id DESC
                               LIMIT @limit;
                               """;
        command.Parameters.AddWithValue("limit", limit);
        var result = new List<InspectionReview>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadReview(reader));
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, InspectionReview>> GetLatestByInspectionRecordIdsAsync(
        IReadOnlyCollection<Guid> inspectionRecordIds,
        CancellationToken ct = default)
    {
        var ids = inspectionRecordIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, InspectionReview>();
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"""
             SELECT DISTINCT ON (inspection_record_id)
                    review_id, inspection_record_id, execution_id, decision,
                    reviewed_at, reviewed_by, notes
             FROM inspection_reviews
             WHERE inspection_record_id = ANY(@ids)
             ORDER BY inspection_record_id, reviewed_at DESC, review_id DESC;
             """);
        command.Parameters.AddWithValue("ids", ids);
        var result = new Dictionary<Guid, InspectionReview>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var review = ReadReview(reader);
            result[review.InspectionRecordId] = review;
        }
        return result;
    }

    public async Task LogAccessAsync(
        Guid? inspectionRecordId,
        Guid? attachmentId,
        string action,
        string actor,
        string? detail,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO inspection_audit_log(
              inspection_record_id, attachment_id, action, actor, detail)
            VALUES (@inspection_record_id, @attachment_id, @action, @actor, @detail);
            """);
        command.Parameters.AddWithValue("inspection_record_id", (object?)inspectionRecordId ?? DBNull.Value);
        command.Parameters.AddWithValue("attachment_id", (object?)attachmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("detail", (object?)detail ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(
        Guid? inspectionRecordId,
        Guid? attachmentId,
        int limit,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand();
        var predicates = new List<string>();
        if (inspectionRecordId.HasValue)
        {
            predicates.Add("inspection_record_id = @inspection_record_id");
            command.Parameters.AddWithValue("inspection_record_id", inspectionRecordId.Value);
        }
        if (attachmentId.HasValue)
        {
            predicates.Add("attachment_id = @attachment_id");
            command.Parameters.AddWithValue("attachment_id", attachmentId.Value);
        }
        command.CommandText = $"""
                               SELECT audit_id, inspection_record_id, attachment_id, action, occurred_at, actor, detail
                               FROM inspection_audit_log
                               {(predicates.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", predicates)}")}
                               ORDER BY occurred_at DESC, audit_id DESC
                               LIMIT @limit;
                               """;
        command.Parameters.AddWithValue("limit", limit);
        var result = new List<InspectionAuditEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new InspectionAuditEntry
            {
                AuditId = reader.GetInt64(0),
                InspectionRecordId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                AttachmentId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Action = reader.GetString(3),
                OccurredAt = new DateTimeOffset(reader.GetDateTime(4).ToUniversalTime()),
                Actor = reader.GetString(5),
                Detail = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return result;
    }


    private async Task<StoredReview?> GetWithHashAsync(Guid reviewId, CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT review_id, inspection_record_id, execution_id, decision,
                   reviewed_at, reviewed_by, notes, payload_hash
            FROM inspection_reviews
            WHERE review_id = @review_id;
            """);
        command.Parameters.AddWithValue("review_id", reviewId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;
        return new StoredReview(ReadReview(reader), reader.GetString(7));
    }

    private static InspectionReview ReadReview(NpgsqlDataReader reader)
        => new()
        {
            ReviewId = reader.GetGuid(0),
            InspectionRecordId = reader.GetGuid(1),
            ExecutionId = reader.GetString(2),
            Decision = reader.GetString(3),
            ReviewedAt = new DateTimeOffset(reader.GetDateTime(4).ToUniversalTime()),
            ReviewedBy = reader.GetString(5),
            Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
        };

    private static string ComputeHash(
        CreateInspectionReviewRequest request,
        string executionId,
        string reviewedBy)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { request, executionId, reviewedBy }, JsonOptions)));

    private sealed record StoredReview(InspectionReview Review, string PayloadHash);

    private const string SelectColumns = """
        SELECT review_id, inspection_record_id, execution_id, decision,
               reviewed_at, reviewed_by, notes
        FROM inspection_reviews
        """;
}
