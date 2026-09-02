using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

/// <summary>
/// 在 PostgreSQL 内先完成项目、站点和复核状态过滤，再融合词法和向量候选。
/// </summary>
public sealed class PostgresProcessKnowledgeSearch(
    NpgsqlDataSource dataSource,
    IKnowledgeEmbeddingClient embeddings) : IProcessKnowledgeSearch
{
    public async Task<ProcessKnowledgeSearchResult> SearchAsync(
        ProcessKnowledgeSearchRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        var limitations = new List<string>();
        KnowledgeEmbedding? queryEmbedding = null;
        if (embeddings.IsConfigured)
        {
            try
            {
                queryEmbedding = await embeddings.EmbedAsync(request.Query, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (ShouldFallbackToKeyword(exception, ct))
            {
                limitations.Add("语义检索暂时不可用，已使用关键词检索。 ");
            }
        }

        var matches = await QueryAsync(request, queryEmbedding, ct).ConfigureAwait(false);
        if (matches.Count == 0 && queryEmbedding is null && limitations.Count == 0 && embeddings.IsConfigured)
            limitations.Add("当前没有可用的语义检索结果，已使用关键词检索。 ");
        return new ProcessKnowledgeSearchResult
        {
            Hits = matches,
            RetrievalMode = queryEmbedding is null ? "keyword" : "hybrid",
            Limitations = limitations
        };
    }

    internal static bool ShouldFallbackToKeyword(Exception exception, CancellationToken callerToken)
        => exception is not OperationCanceledException || !callerToken.IsCancellationRequested;

    private async Task<IReadOnlyList<ProcessKnowledgeSearchHit>> QueryAsync(
        ProcessKnowledgeSearchRequest request,
        KnowledgeEmbedding? embedding,
        CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH eligible AS (
              SELECT
                source.source_id, source.title, source.source_kind, source.status, source.storage_ref,
                source.sha256, source.media_type, source.file_name, source.size_bytes, source.uploaded_by,
                source.uploaded_at, source.reviewed_by AS source_reviewed_by, source.reviewed_at AS source_reviewed_at,
                source.extraction_status, source.extraction_error, source.extractor_version AS source_extractor_version,
                fragment.record_id, fragment.category, fragment.page_or_sheet, fragment.region, fragment.content,
                fragment.human_reviewed, fragment.created_by, fragment.created_at,
                fragment.reviewed_by AS fragment_reviewed_by, fragment.reviewed_at AS fragment_reviewed_at,
                fragment.extraction_method, fragment.extractor_version AS fragment_extractor_version,
                fragment.extraction_confidence, fragment.location_kind, fragment.page_number,
                fragment.sheet_name, fragment.cell_range, fragment.citation_region, fragment.content_hash,
                GREATEST(
                  similarity(lower(concat_ws(' ', source.title, fragment.category, fragment.content)), lower(@query)),
                  0) +
                  COALESCE(ts_rank_cd(
                    to_tsvector('simple', concat_ws(' ', source.title, fragment.category, fragment.content)),
                    websearch_to_tsquery('simple', @query)), 0) AS keyword_score,
                CASE WHEN NULLIF(@embedding, '') IS NULL OR vector.embedding_model <> @embedding_model
                           OR vector.content_hash <> COALESCE(fragment.content_hash, '') THEN 0
                  ELSE GREATEST(0, 1 - (vector.embedding <=> NULLIF(@embedding, '')::vector)) END AS semantic_score
              FROM knowledge_sources source
              JOIN process_research_projects project ON project.project_id=source.project_id
              JOIN knowledge_fragments fragment ON fragment.source_id=source.source_id
              LEFT JOIN knowledge_fragment_embeddings vector ON vector.record_id=fragment.record_id
              WHERE source.project_id=@project_id
                AND source.status='reviewed'
                AND fragment.human_reviewed
                AND (@allow_all_sites OR lower(COALESCE(project.payload->>'siteCode', '')) = ANY(@site_ids))
                AND (@product_family_code IS NULL OR NOT EXISTS (
                  SELECT 1 FROM knowledge_source_context context
                  WHERE context.source_id=source.source_id AND context.dimension_code='product_family_code') OR EXISTS (
                  SELECT 1 FROM knowledge_source_context context
                  WHERE context.source_id=source.source_id AND context.dimension_code='product_family_code'
                    AND lower(context.dimension_value)=lower(@product_family_code)))
                AND (@equipment_id IS NULL OR NOT EXISTS (
                  SELECT 1 FROM knowledge_source_context context
                  WHERE context.source_id=source.source_id AND context.dimension_code='equipment_id') OR EXISTS (
                  SELECT 1 FROM knowledge_source_context context
                  WHERE context.source_id=source.source_id AND context.dimension_code='equipment_id'
                    AND lower(context.dimension_value)=lower(@equipment_id)))
            )
            SELECT *, keyword_score * 0.55 + semantic_score * 0.45 AS score
            FROM eligible
            WHERE keyword_score > 0.01 OR semantic_score > 0
            ORDER BY score DESC, fragment_reviewed_at DESC NULLS LAST, record_id
            LIMIT @limit;
            """);
        command.Parameters.AddWithValue("query", request.Query.Trim());
        command.Parameters.AddWithValue("project_id", request.ResearchProjectId);
        command.Parameters.AddWithValue("allow_all_sites", request.AllowAllSites);
        command.Parameters.AddWithValue(
            "site_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            request.SiteIds.Select(static value => value.Trim().ToLowerInvariant())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        AddNullable(command, "product_family_code", request.ProductFamilyCode);
        AddNullable(command, "equipment_id", request.EquipmentId);
        command.Parameters.AddWithValue("embedding_model", embedding?.Model ?? "");
        command.Parameters.AddWithValue("embedding", embedding is null
            ? ""
            : PostgresKnowledgeEmbeddingJobStore.ToVectorLiteral(embedding.Values));
        command.Parameters.AddWithValue("limit", Math.Clamp(request.Limit, 1, 20));

        var candidates = new List<Candidate>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            candidates.Add(ReadCandidate(reader));
        if (candidates.Count == 0)
            return [];

        var valuesByRecord = await ReadStructuredValuesAsync(
            candidates.Select(static value => value.Record.RecordId).ToArray(), ct).ConfigureAwait(false);
        return candidates.Select(candidate => new ProcessKnowledgeSearchHit
        {
            Source = candidate.Source,
            Record = candidate.Record with
            {
                StructuredValues = valuesByRecord.GetValueOrDefault(candidate.Record.RecordId)
                    ?? new Dictionary<string, string>()
            },
            Score = candidate.Score,
            RetrievalMethod = candidate.SemanticScore > 0 && candidate.KeywordScore > 0.01
                ? "hybrid"
                : candidate.SemanticScore > 0 ? "semantic" : "keyword"
        }).ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>>> ReadStructuredValuesAsync(
        Guid[] recordIds,
        CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT fragment_id, value_code, value_text
            FROM knowledge_fragment_values
            WHERE fragment_id = ANY(@record_ids);
            """);
        command.Parameters.AddWithValue("record_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, recordIds);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new Dictionary<Guid, Dictionary<string, string>>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var recordId = reader.GetGuid(0);
            if (!values.TryGetValue(recordId, out var recordValues))
                values[recordId] = recordValues = new Dictionary<string, string>(StringComparer.Ordinal);
            recordValues[reader.GetString(1)] = reader.GetString(2);
        }
        return values.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyDictionary<string, string>)pair.Value);
    }

    private static Candidate ReadCandidate(NpgsqlDataReader reader)
    {
        var citation = reader.IsDBNull(29) ? null : new KnowledgeCitation
        {
            LocationKind = reader.GetString(29),
            PageNumber = reader.IsDBNull(30) ? null : reader.GetInt32(30),
            SheetName = reader.IsDBNull(31) ? null : reader.GetString(31),
            CellRange = reader.IsDBNull(32) ? null : reader.GetString(32),
            Region = reader.IsDBNull(33) ? null : reader.GetString(33),
            ContentHash = reader.IsDBNull(34) ? "" : reader.GetString(34)
        };
        var source = new KnowledgeSource
        {
            SourceId = reader.GetGuid(0),
            Title = reader.GetString(1),
            SourceKind = reader.GetString(2),
            Status = reader.GetString(3),
            StorageRef = reader.GetString(4),
            Sha256 = reader.GetString(5),
            MediaType = reader.GetString(6),
            FileName = reader.GetString(7),
            SizeBytes = reader.GetInt64(8),
            UploadedBy = reader.GetString(9),
            UploadedAt = reader.GetFieldValue<DateTimeOffset>(10),
            ReviewedBy = reader.IsDBNull(11) ? null : reader.GetString(11),
            ReviewedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            ExtractionStatus = reader.GetString(13),
            ExtractionError = reader.IsDBNull(14) ? null : reader.GetString(14),
            ExtractorVersion = reader.IsDBNull(15) ? null : reader.GetString(15)
        };
        var record = new KnowledgeRecord
        {
            RecordId = reader.GetGuid(16),
            SourceId = source.SourceId,
            Category = reader.GetString(17),
            PageOrSheet = reader.IsDBNull(18) ? null : reader.GetString(18),
            Region = reader.IsDBNull(19) ? null : reader.GetString(19),
            Content = reader.GetString(20),
            HumanReviewed = reader.GetBoolean(21),
            CreatedBy = reader.GetString(22),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(23),
            ReviewedBy = reader.IsDBNull(24) ? null : reader.GetString(24),
            ReviewedAt = reader.IsDBNull(25) ? null : reader.GetFieldValue<DateTimeOffset>(25),
            ExtractionMethod = reader.GetString(26),
            ExtractorVersion = reader.GetString(27),
            ExtractionConfidence = reader.IsDBNull(28) ? null : reader.GetDouble(28),
            Citation = citation
        };
        return new Candidate(source, record, reader.GetDouble(35), reader.GetDouble(36), reader.GetDouble(37));
    }

    private static void AddNullable(NpgsqlCommand command, string name, string? value)
        => command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim());

    private sealed record Candidate(
        KnowledgeSource Source,
        KnowledgeRecord Record,
        double KeywordScore,
        double SemanticScore,
        double Score);
}
