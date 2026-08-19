using Ingot.Platform.Application.ResearchAssets;
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ResearchAssets;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class PostgresResearchAssetStore : IResearchAssetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly ProcessKnowledgeOptions _options;

    public PostgresResearchAssetStore(
        NpgsqlDataSource dataSource,
        IOptions<ProcessKnowledgeOptions> options)
    {
        _dataSource = dataSource;
        _options = options.Value;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(GetRootPath());
        if (GetArchiveRootPath() is { } archiveRoot)
            Directory.CreateDirectory(archiveRoot);
        return Task.CompletedTask;
    }
    public async Task<TrainingDatasetVersion> AddDatasetAsync(
        TrainingDatasetVersion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO training_dataset_versions(dataset_id, version, payload, created_at)
            VALUES (@dataset_id, @version, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("dataset_id", value.DatasetId);
        command.Parameters.AddWithValue("version", value.Version);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<TrainingDatasetVersion?> GetDatasetAsync(
        string datasetId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<TrainingDatasetVersion>(
            "SELECT payload::text FROM training_dataset_versions WHERE dataset_id = @id AND version = @version;",
            command =>
            {
                command.Parameters.AddWithValue("id", datasetId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<TrainingDatasetVersion>> ListDatasetsAsync(CancellationToken ct = default)
        => ListAsync<TrainingDatasetVersion>(
            "SELECT payload::text FROM training_dataset_versions ORDER BY created_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<ProcessModelVersion> SaveModelAsync(
        ProcessModelVersion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_model_versions(
              model_id, version, status, dataset_id, dataset_version, payload, updated_at)
            VALUES (@model_id, @version, @status, @dataset_id, @dataset_version, @payload, @updated_at)
            ON CONFLICT (model_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              dataset_id = EXCLUDED.dataset_id,
              dataset_version = EXCLUDED.dataset_version,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("model_id", value.ModelId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("dataset_id", value.DatasetId);
        command.Parameters.AddWithValue("dataset_version", value.DatasetVersion);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ProcessModelVersion?> GetModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<ProcessModelVersion>(
            "SELECT payload::text FROM process_model_versions WHERE model_id = @id AND version = @version;",
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<ProcessModelVersion>> ListModelsAsync(CancellationToken ct = default)
        => ListAsync<ProcessModelVersion>(
            "SELECT payload::text FROM process_model_versions ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<ModelEvaluation> AddEvaluationAsync(
        ModelEvaluation value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO model_evaluations(
              evaluation_id, model_id, model_version, passed, payload, created_at)
            VALUES (@id, @model_id, @model_version, @passed, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.EvaluationId);
        command.Parameters.AddWithValue("model_id", value.ModelId);
        command.Parameters.AddWithValue("model_version", value.ModelVersion);
        command.Parameters.AddWithValue("passed", value.Passed);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.EvaluatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ModelEvaluation>> ListEvaluationsAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => ListAsync<ModelEvaluation>(
            """
            SELECT payload::text FROM model_evaluations
            WHERE model_id = @id AND model_version = @version
            ORDER BY created_at DESC
            LIMIT 200;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public async Task<ModelDriftReading> AddDriftReadingAsync(
        ModelDriftReading value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO model_drift_readings(
              reading_id, model_id, model_version, value, stop_threshold, payload, created_at)
            VALUES (@id, @model_id, @model_version, @value, @stop_threshold, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.ReadingId);
        command.Parameters.AddWithValue("model_id", value.ModelId);
        command.Parameters.AddWithValue("model_version", value.ModelVersion);
        command.Parameters.AddWithValue("value", value.Value);
        command.Parameters.AddWithValue("stop_threshold", value.StopThreshold);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.RecordedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ModelDriftReading>> ListDriftReadingsAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => ListAsync<ModelDriftReading>(
            """
            SELECT payload::text FROM model_drift_readings
            WHERE model_id = @id AND model_version = @version
            ORDER BY created_at DESC
            LIMIT 200;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public async Task<MechanismModelVersion> SaveMechanismModelAsync(
        MechanismModelVersion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO mechanism_model_versions(
              model_id, version, status, content_hash, payload, updated_at)
            VALUES (@id, @version, @status, @content_hash, @payload, @updated_at)
            ON CONFLICT (model_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              content_hash = EXCLUDED.content_hash,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.ModelId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("content_hash", value.ContentHash);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<MechanismModelVersion?> GetMechanismModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<MechanismModelVersion>(
            """
            SELECT payload::text FROM mechanism_model_versions
            WHERE model_id = @id AND version = @version;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(
        CancellationToken ct = default)
        => ListAsync<MechanismModelVersion>(
            "SELECT payload::text FROM mechanism_model_versions ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<MechanismFusionDefinition> SaveMechanismFusionAsync(
        MechanismFusionDefinition value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO mechanism_fusion_definitions(
              fusion_id, version, status, mode, mechanism_model_id,
              mechanism_model_version, content_hash, payload, updated_at)
            VALUES (
              @id, @version, @status, @mode, @model_id,
              @model_version, @content_hash, @payload, @updated_at)
            ON CONFLICT (fusion_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              mode = EXCLUDED.mode,
              mechanism_model_id = EXCLUDED.mechanism_model_id,
              mechanism_model_version = EXCLUDED.mechanism_model_version,
              content_hash = EXCLUDED.content_hash,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.FusionId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("mode", value.Mode);
        command.Parameters.AddWithValue("model_id", value.MechanismModelId);
        command.Parameters.AddWithValue("model_version", value.MechanismModelVersion);
        command.Parameters.AddWithValue("content_hash", value.ContentHash);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<MechanismFusionDefinition?> GetMechanismFusionAsync(
        string fusionId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<MechanismFusionDefinition>(
            """
            SELECT payload::text FROM mechanism_fusion_definitions
            WHERE fusion_id = @id AND version = @version;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", fusionId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(
        CancellationToken ct = default)
        => ListAsync<MechanismFusionDefinition>(
            "SELECT payload::text FROM mechanism_fusion_definitions ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<DatasetQualityValidationReport> SaveDatasetQualityValidationReportAsync(
        DatasetQualityValidationReport value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO dataset_quality_validation_reports(
              report_id, dataset_id, dataset_version, industry, status,
              source_sha256, payload, created_at)
            VALUES (
              @id, @dataset_id, @dataset_version, @industry, @status,
              @source_sha256, @payload, @created_at)
            ON CONFLICT (report_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload;
            """);
        command.Parameters.AddWithValue("id", value.ReportId);
        command.Parameters.AddWithValue("dataset_id", value.DatasetId);
        command.Parameters.AddWithValue("dataset_version", value.DatasetVersion);
        command.Parameters.AddWithValue("industry", value.Industry);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("source_sha256", value.SourceSha256);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<DatasetQualityValidationReport>> ListDatasetQualityValidationReportsAsync(
        CancellationToken ct = default)
        => ListAsync<DatasetQualityValidationReport>(
            "SELECT payload::text FROM dataset_quality_validation_reports ORDER BY created_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<KnowledgeSource> AddKnowledgeSourceAsync(
        Stream content,
        string title,
        string sourceKind,
        string fileName,
        string mediaType,
        IReadOnlyDictionary<string, string> contextSelector,
        string userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await InitializeAsync(ct).ConfigureAwait(false);
        var safeFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 255)
            throw new ArgumentException("文件名不能为空且最长 255 个字符。", nameof(fileName));
        var tempPath = Path.Combine(GetRootPath(), $"{Guid.CreateVersion7():N}.uploading");
        long size = 0;
        string hash;
        using var sha = SHA256.Create();
        try
        {
            await using var temp = File.Create(tempPath);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await content.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                size += read;
                if (size > _options.MaxFileBytes)
                    throw new InvalidDataException($"知识来源文件超过 {_options.MaxFileBytes} 字节上限。");
                sha.TransformBlock(buffer, 0, read, null, 0);
                await temp.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            sha.TransformFinalBlock([], 0, 0);
            hash = Convert.ToHexStringLower(sha.Hash!);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
        if (size <= 0)
        {
            File.Delete(tempPath);
            throw new InvalidDataException("知识来源文件不能为空。");
        }

        var finalPath = GetKnowledgePath(hash, safeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (!File.Exists(finalPath))
            File.Move(tempPath, finalPath);
        else
            File.Delete(tempPath);
        if (GetArchivePath(hash, safeFileName) is { } archivePath)
            await CopyToArchiveAsync(finalPath, archivePath, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var source = new KnowledgeSource
        {
            SourceId = Guid.CreateVersion7(),
            Title = title.Trim(),
            SourceKind = sourceKind.Trim().ToLowerInvariant(),
            Status = KnowledgeSourceStatuses.Uploaded,
            StorageRef = $"process-knowledge://sha256/{hash}/{Uri.EscapeDataString(safeFileName)}",
            Sha256 = hash,
            MediaType = string.IsNullOrWhiteSpace(mediaType)
                ? "application/octet-stream"
                : mediaType.Trim().ToLowerInvariant(),
            FileName = safeFileName,
            SizeBytes = size,
            ContextSelector = contextSelector,
            UploadedBy = userId,
            UploadedAt = now
        };

        if (!contextSelector.TryGetValue("research-project-id", out var projectIdText) ||
            !Guid.TryParse(projectIdText, out var projectId))
            throw new InvalidDataException("知识来源必须绑定研发项目。");
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        var insertedSource = false;
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO knowledge_sources(
              source_id, project_id, title, source_kind, status, storage_ref, sha256,
              media_type, file_name, size_bytes, extraction_status, extractor_version,
              uploaded_by, uploaded_at, updated_at)
            VALUES (
              @id, @project_id, @title, @source_kind, @status, @storage_ref, @sha256,
              @media_type, @file_name, @size_bytes, @extraction_status, @extractor_version,
              @uploaded_by, @uploaded_at, @updated_at)
            ON CONFLICT (project_id, sha256) DO NOTHING;
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", source.SourceId);
            insert.Parameters.AddWithValue("project_id", projectId);
            insert.Parameters.AddWithValue("title", source.Title);
            insert.Parameters.AddWithValue("source_kind", source.SourceKind);
            insert.Parameters.AddWithValue("status", source.Status);
            insert.Parameters.AddWithValue("storage_ref", source.StorageRef);
            insert.Parameters.AddWithValue("sha256", source.Sha256);
            insert.Parameters.AddWithValue("media_type", source.MediaType);
            insert.Parameters.AddWithValue("file_name", source.FileName);
            insert.Parameters.AddWithValue("size_bytes", source.SizeBytes);
            insert.Parameters.AddWithValue("extraction_status", source.ExtractionStatus);
            AddNullable(insert, "extractor_version", NpgsqlDbType.Text, null);
            insert.Parameters.AddWithValue("uploaded_by", source.UploadedBy);
            insert.Parameters.AddWithValue("uploaded_at", source.UploadedAt);
            insert.Parameters.AddWithValue("updated_at", now);
            if (await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
            {
                insertedSource = true;
                foreach (var (code, value) in contextSelector)
                {
                    await using var context = new NpgsqlCommand(
                        "INSERT INTO knowledge_source_context(source_id, dimension_code, dimension_value) VALUES (@id, @code, @value);",
                        connection, transaction);
                    context.Parameters.AddWithValue("id", source.SourceId);
                    context.Parameters.AddWithValue("code", code);
                    context.Parameters.AddWithValue("value", value);
                    await context.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
        }
        if (insertedSource)
        {
            await using var job = new NpgsqlCommand(
                """
                INSERT INTO knowledge_extraction_jobs(source_id,requested_by,status,available_at,updated_at)
                VALUES(@source_id,@user_id,'queued',now(),now());
                """, connection, transaction);
            job.Parameters.AddWithValue("source_id", source.SourceId);
            job.Parameters.AddWithValue("user_id", userId);
            await job.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return await GetKnowledgeSourceByHashAsync(hash, projectId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("知识来源写入后无法读取。");
    }

    public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default)
        => ReadKnowledgeSourceAsync(sourceId, null, null, ct);

    public async Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT source_id FROM knowledge_sources ORDER BY updated_at DESC LIMIT 200;");
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) ids.Add(reader.GetGuid(0));
        var values = new List<KnowledgeSource>(ids.Count);
        foreach (var id in ids)
            if (await ReadKnowledgeSourceAsync(id, null, null, ct).ConfigureAwait(false) is { } value) values.Add(value);
        return values;
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT source_id FROM knowledge_sources WHERE project_id=@project_id ORDER BY updated_at DESC;");
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) ids.Add(reader.GetGuid(0));
        var values = new List<KnowledgeSource>(ids.Count);
        foreach (var id in ids)
            if (await ReadKnowledgeSourceAsync(id, null, null, ct).ConfigureAwait(false) is { } value) values.Add(value);
        return values;
    }

    public async Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default)
    {
        var source = await GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
            return null;
        var path = GetKnowledgePath(source.Sha256, source.FileName);
        if (!File.Exists(path) && GetArchivePath(source.Sha256, source.FileName) is { } archivePath)
            path = archivePath;
        if (!File.Exists(path))
            return null;
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public async Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(
        KnowledgeSource value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE knowledge_sources SET
              title = @title, source_kind = @source_kind, status = @status,
              extraction_status = @extraction_status, extraction_error = @extraction_error,
              extractor_version = @extractor_version, reviewed_by = @reviewed_by,
              reviewed_at = @reviewed_at, updated_at = @updated_at
            WHERE source_id = @id;
            """);
        command.Parameters.AddWithValue("id", value.SourceId);
        command.Parameters.AddWithValue("title", value.Title);
        command.Parameters.AddWithValue("source_kind", value.SourceKind);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("extraction_status", value.ExtractionStatus);
        AddNullable(command, "extraction_error", NpgsqlDbType.Text, value.ExtractionError);
        AddNullable(command, "extractor_version", NpgsqlDbType.Text, value.ExtractorVersion);
        AddNullable(command, "reviewed_by", NpgsqlDbType.Text, value.ReviewedBy);
        AddNullable(command, "reviewed_at", NpgsqlDbType.TimestampTz, value.ReviewedAt);
        command.Parameters.AddWithValue("updated_at", value.ReviewedAt ?? DateTimeOffset.UtcNow);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new KeyNotFoundException("知识来源不存在。");
        return value;
    }

    public async Task<KnowledgeRecord> SaveKnowledgeRecordAsync(
        KnowledgeRecord value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO knowledge_fragments(
              record_id, source_id, category, page_or_sheet, region, content, human_reviewed,
              created_by, created_at, reviewed_by, reviewed_at, extraction_method,
              extractor_version, extraction_confidence, location_kind, page_number,
              sheet_name, cell_range, citation_region, content_hash, updated_at)
            VALUES (
              @id, @source_id, @category, @page_or_sheet, @region, @content, @human_reviewed,
              @created_by, @created_at, @reviewed_by, @reviewed_at, @extraction_method,
              @extractor_version, @confidence, @location_kind, @page_number,
              @sheet_name, @cell_range, @citation_region, @content_hash, @updated_at)
            ON CONFLICT (record_id) DO UPDATE SET
              category=EXCLUDED.category, page_or_sheet=EXCLUDED.page_or_sheet,
              region=EXCLUDED.region, content=EXCLUDED.content,
              human_reviewed=EXCLUDED.human_reviewed, reviewed_by=EXCLUDED.reviewed_by,
              reviewed_at=EXCLUDED.reviewed_at, extraction_method=EXCLUDED.extraction_method,
              extractor_version=EXCLUDED.extractor_version, extraction_confidence=EXCLUDED.extraction_confidence,
              location_kind=EXCLUDED.location_kind, page_number=EXCLUDED.page_number,
              sheet_name=EXCLUDED.sheet_name, cell_range=EXCLUDED.cell_range,
              citation_region=EXCLUDED.citation_region, content_hash=EXCLUDED.content_hash,
              updated_at=EXCLUDED.updated_at;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", value.RecordId);
            command.Parameters.AddWithValue("source_id", value.SourceId);
            command.Parameters.AddWithValue("category", value.Category);
            AddNullable(command, "page_or_sheet", NpgsqlDbType.Text, value.PageOrSheet);
            AddNullable(command, "region", NpgsqlDbType.Text, value.Region);
            command.Parameters.AddWithValue("content", value.Content);
            command.Parameters.AddWithValue("human_reviewed", value.HumanReviewed);
            command.Parameters.AddWithValue("created_by", value.CreatedBy);
            command.Parameters.AddWithValue("created_at", value.CreatedAt);
            AddNullable(command, "reviewed_by", NpgsqlDbType.Text, value.ReviewedBy);
            AddNullable(command, "reviewed_at", NpgsqlDbType.TimestampTz, value.ReviewedAt);
            command.Parameters.AddWithValue("extraction_method", value.ExtractionMethod);
            command.Parameters.AddWithValue("extractor_version", value.ExtractorVersion);
            AddNullable(command, "confidence", NpgsqlDbType.Double, value.ExtractionConfidence);
            AddNullable(command, "location_kind", NpgsqlDbType.Text, value.Citation?.LocationKind);
            AddNullable(command, "page_number", NpgsqlDbType.Integer, value.Citation?.PageNumber);
            AddNullable(command, "sheet_name", NpgsqlDbType.Text, value.Citation?.SheetName);
            AddNullable(command, "cell_range", NpgsqlDbType.Text, value.Citation?.CellRange);
            AddNullable(command, "citation_region", NpgsqlDbType.Text, value.Citation?.Region);
            AddNullable(command, "content_hash", NpgsqlDbType.Text, value.Citation?.ContentHash);
            command.Parameters.AddWithValue("updated_at", value.ReviewedAt ?? value.CreatedAt);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM knowledge_fragment_values WHERE fragment_id = @id;", connection, transaction))
        {
            delete.Parameters.AddWithValue("id", value.RecordId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var (code, text) in value.StructuredValues)
        {
            await using var structured = new NpgsqlCommand(
                "INSERT INTO knowledge_fragment_values(fragment_id, value_code, value_text) VALUES (@id,@code,@value);",
                connection, transaction);
            structured.Parameters.AddWithValue("id", value.RecordId);
            structured.Parameters.AddWithValue("code", code);
            structured.Parameters.AddWithValue("value", text);
            await structured.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public async Task<KnowledgeSource> ReplaceExtractedKnowledgeRecordsAsync(
        KnowledgeSource source,
        IReadOnlyList<KnowledgeRecord> records,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM knowledge_fragments WHERE source_id=@source_id;", connection, transaction))
        {
            delete.Parameters.AddWithValue("source_id", source.SourceId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var value in records)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO knowledge_fragments(
                  record_id,source_id,category,page_or_sheet,region,content,human_reviewed,
                  created_by,created_at,reviewed_by,reviewed_at,extraction_method,extractor_version,
                  extraction_confidence,location_kind,page_number,sheet_name,cell_range,
                  citation_region,content_hash,updated_at)
                VALUES(@id,@source_id,@category,@page_or_sheet,@region,@content,@human_reviewed,
                  @created_by,@created_at,@reviewed_by,@reviewed_at,@method,@version,@confidence,
                  @location_kind,@page_number,@sheet_name,@cell_range,@citation_region,@content_hash,@updated_at);
                """, connection, transaction);
            insert.Parameters.AddWithValue("id", value.RecordId);
            insert.Parameters.AddWithValue("source_id", source.SourceId);
            insert.Parameters.AddWithValue("category", value.Category);
            AddNullable(insert, "page_or_sheet", NpgsqlDbType.Text, value.PageOrSheet);
            AddNullable(insert, "region", NpgsqlDbType.Text, value.Region);
            insert.Parameters.AddWithValue("content", value.Content);
            insert.Parameters.AddWithValue("human_reviewed", value.HumanReviewed);
            insert.Parameters.AddWithValue("created_by", value.CreatedBy);
            insert.Parameters.AddWithValue("created_at", value.CreatedAt);
            AddNullable(insert, "reviewed_by", NpgsqlDbType.Text, value.ReviewedBy);
            AddNullable(insert, "reviewed_at", NpgsqlDbType.TimestampTz, value.ReviewedAt);
            insert.Parameters.AddWithValue("method", value.ExtractionMethod);
            insert.Parameters.AddWithValue("version", value.ExtractorVersion);
            AddNullable(insert, "confidence", NpgsqlDbType.Double, value.ExtractionConfidence);
            AddNullable(insert, "location_kind", NpgsqlDbType.Text, value.Citation?.LocationKind);
            AddNullable(insert, "page_number", NpgsqlDbType.Integer, value.Citation?.PageNumber);
            AddNullable(insert, "sheet_name", NpgsqlDbType.Text, value.Citation?.SheetName);
            AddNullable(insert, "cell_range", NpgsqlDbType.Text, value.Citation?.CellRange);
            AddNullable(insert, "citation_region", NpgsqlDbType.Text, value.Citation?.Region);
            AddNullable(insert, "content_hash", NpgsqlDbType.Text, value.Citation?.ContentHash);
            insert.Parameters.AddWithValue("updated_at", value.CreatedAt);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            foreach (var (code, text) in value.StructuredValues)
            {
                await using var structured = new NpgsqlCommand(
                    "INSERT INTO knowledge_fragment_values(fragment_id,value_code,value_text) VALUES(@id,@code,@text);",
                    connection, transaction);
                structured.Parameters.AddWithValue("id", value.RecordId);
                structured.Parameters.AddWithValue("code", code);
                structured.Parameters.AddWithValue("text", text);
                await structured.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        await using (var update = new NpgsqlCommand(
            """
            UPDATE knowledge_sources SET status=@status,extraction_status=@extraction_status,
              extraction_error=@error,extractor_version=@version,updated_at=@updated_at
            WHERE source_id=@id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("id", source.SourceId);
            update.Parameters.AddWithValue("status", source.Status);
            update.Parameters.AddWithValue("extraction_status", source.ExtractionStatus);
            AddNullable(update, "error", NpgsqlDbType.Text, source.ExtractionError);
            AddNullable(update, "version", NpgsqlDbType.Text, source.ExtractorVersion);
            update.Parameters.AddWithValue("updated_at", DateTimeOffset.UtcNow);
            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new KeyNotFoundException("知识来源不存在。");
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return source;
    }

    public async Task EnqueueKnowledgeExtractionAsync(Guid sourceId, string userId, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            WITH queued AS (
              INSERT INTO knowledge_extraction_jobs(source_id,requested_by,status,available_at,updated_at)
              VALUES(@source_id,@user_id,'queued',now(),now())
              ON CONFLICT(source_id) DO UPDATE SET requested_by=EXCLUDED.requested_by,status='queued',
                attempt_count=0,available_at=now(),lease_id=NULL,leased_at=NULL,last_error=NULL,updated_at=now()
              RETURNING source_id)
            UPDATE knowledge_sources SET extraction_status='pending',extraction_error=NULL,updated_at=now()
            WHERE source_id IN (SELECT source_id FROM queued);
            """);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<KnowledgeExtractionJob?> ClaimKnowledgeExtractionAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default)
    {
        var leaseId = Guid.CreateVersion7();
        await using var command = _dataSource.CreateCommand(
            """
            WITH candidate AS (
              SELECT source_id FROM knowledge_extraction_jobs
              WHERE (status='queued' AND available_at <= now())
                 OR (status='running' AND leased_at < now() - @lease_timeout)
              ORDER BY available_at,updated_at FOR UPDATE SKIP LOCKED LIMIT 1)
            UPDATE knowledge_extraction_jobs job SET status='running',lease_id=@lease_id,leased_at=now(),
              attempt_count=attempt_count+1,updated_at=now()
            FROM candidate WHERE job.source_id=candidate.source_id
            RETURNING job.source_id,job.requested_by,job.attempt_count;
            """);
        command.Parameters.AddWithValue("lease_id", leaseId);
        command.Parameters.AddWithValue("lease_timeout", leaseTimeout);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new KnowledgeExtractionJob(reader.GetGuid(0), reader.GetString(1), leaseId, reader.GetInt32(2))
            : null;
    }

    public async Task<bool> RenewKnowledgeExtractionLeaseAsync(
        Guid sourceId,
        Guid leaseId,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE knowledge_extraction_jobs SET leased_at=now(),updated_at=now()
            WHERE source_id=@source_id AND lease_id=@lease_id AND status='running';
            """);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("lease_id", leaseId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> CompleteKnowledgeExtractionAsync(
        Guid sourceId,
        Guid leaseId,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE knowledge_extraction_jobs SET status='completed',lease_id=NULL,leased_at=NULL,
              last_error=NULL,updated_at=now()
            WHERE source_id=@source_id AND lease_id=@lease_id AND status='running';
            """);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("lease_id", leaseId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<KnowledgeExtractionFailureDisposition?> FailKnowledgeExtractionAsync(
        Guid sourceId,
        Guid leaseId,
        string error,
        bool retryable,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            WITH changed AS (
              UPDATE knowledge_extraction_jobs SET
                status=CASE WHEN @retryable AND attempt_count < @max_attempts
                  THEN 'queued' ELSE 'dead-letter' END,
                available_at=CASE WHEN @retryable AND attempt_count < @max_attempts
                  THEN now() + @retry_delay ELSE available_at END,
                lease_id=NULL,leased_at=NULL,last_error=@error,updated_at=now()
              WHERE source_id=@source_id AND lease_id=@lease_id AND status='running'
              RETURNING status)
            UPDATE knowledge_sources source SET
              extraction_status=CASE WHEN changed.status='queued' THEN 'pending' ELSE 'failed' END,
              extraction_error=@error,updated_at=now()
            FROM changed WHERE source.source_id=@source_id
            RETURNING changed.status;
            """);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("lease_id", leaseId);
        command.Parameters.AddWithValue("retryable", retryable);
        command.Parameters.AddWithValue("max_attempts", Math.Max(1, maxAttempts));
        command.Parameters.AddWithValue("retry_delay", retryDelay);
        command.Parameters.AddWithValue("error", error[..Math.Min(error.Length, 1000)]);
        var status = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return status switch
        {
            "queued" => KnowledgeExtractionFailureDisposition.RetryScheduled,
            "dead-letter" => KnowledgeExtractionFailureDisposition.DeadLettered,
            _ => null
        };
    }

    public async Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(
        Guid sourceId,
        CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT record_id, category, page_or_sheet, region, content, human_reviewed,
              created_by, created_at, reviewed_by, reviewed_at, extraction_method,
              extractor_version, extraction_confidence, location_kind, page_number,
              sheet_name, cell_range, citation_region, content_hash
            FROM knowledge_fragments WHERE source_id = @id ORDER BY updated_at DESC LIMIT 500;
            """);
        command.Parameters.AddWithValue("id", sourceId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<KnowledgeRecord>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var citation = reader.IsDBNull(13) ? null : new KnowledgeCitation
            {
                LocationKind = reader.GetString(13),
                PageNumber = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                SheetName = reader.IsDBNull(15) ? null : reader.GetString(15),
                CellRange = reader.IsDBNull(16) ? null : reader.GetString(16),
                Region = reader.IsDBNull(17) ? null : reader.GetString(17),
                ContentHash = reader.IsDBNull(18) ? "" : reader.GetString(18)
            };
            rows.Add(new KnowledgeRecord
            {
                RecordId=reader.GetGuid(0), SourceId=sourceId, Category=reader.GetString(1),
                PageOrSheet=reader.IsDBNull(2)?null:reader.GetString(2), Region=reader.IsDBNull(3)?null:reader.GetString(3),
                Content=reader.GetString(4), HumanReviewed=reader.GetBoolean(5), CreatedBy=reader.GetString(6),
                CreatedAt=reader.GetFieldValue<DateTimeOffset>(7), ReviewedBy=reader.IsDBNull(8)?null:reader.GetString(8),
                ReviewedAt=reader.IsDBNull(9)?null:reader.GetFieldValue<DateTimeOffset>(9), ExtractionMethod=reader.GetString(10),
                ExtractorVersion=reader.GetString(11), ExtractionConfidence=reader.IsDBNull(12)?null:reader.GetDouble(12), Citation=citation
            });
        }
        foreach (var index in Enumerable.Range(0, rows.Count))
            rows[index] = rows[index] with { StructuredValues = await ReadFragmentValuesAsync(rows[index].RecordId, ct).ConfigureAwait(false) };
        return rows;
    }

    public async Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO research_asset_audit(
              entry_id, resource_type, resource_id, action, payload, created_at)
            VALUES (@id, @resource_type, @resource_id, @action, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.EntryId);
        command.Parameters.AddWithValue("resource_type", value.ResourceType);
        command.Parameters.AddWithValue("resource_id", value.ResourceId);
        command.Parameters.AddWithValue("action", value.Action);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(
        string resourceType,
        string resourceId,
        CancellationToken ct = default)
        => ListAsync<ResearchAssetAuditEntry>(
            """
            SELECT payload::text FROM research_asset_audit
            WHERE resource_type = @resource_type AND resource_id = @resource_id
            ORDER BY created_at;
            """,
            command =>
            {
                command.Parameters.AddWithValue("resource_type", resourceType);
                command.Parameters.AddWithValue("resource_id", resourceId);
            },
            ct);

    private async Task<T?> GetSingleAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configure,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(sql);
        configure?.Invoke(command);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return payload is null ? default : Deserialize<T>(payload);
    }

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configure,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(sql);
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private async Task<KnowledgeSource?> GetKnowledgeSourceByHashAsync(
        string sha256,
        Guid projectId,
        CancellationToken ct)
        => await ReadKnowledgeSourceAsync(null, sha256, projectId, ct).ConfigureAwait(false);

    private async Task<KnowledgeSource?> ReadKnowledgeSourceAsync(
        Guid? sourceId,
        string? sha256,
        Guid? projectId,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT source_id, title, source_kind, status, storage_ref, sha256, media_type,
              file_name, size_bytes, uploaded_by, uploaded_at, reviewed_by, reviewed_at,
              extraction_status, extraction_error, extractor_version
            FROM knowledge_sources
            WHERE (@source_id IS NOT NULL AND source_id = @source_id)
               OR (@sha256 IS NOT NULL AND sha256 = @sha256 AND project_id = @project_id)
            LIMIT 1;
            """);
        AddNullable(command, "source_id", NpgsqlDbType.Uuid, sourceId);
        AddNullable(command, "sha256", NpgsqlDbType.Text, sha256);
        AddNullable(command, "project_id", NpgsqlDbType.Uuid, projectId);
        KnowledgeSource? value;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            value = new KnowledgeSource
            {
                SourceId=reader.GetGuid(0), Title=reader.GetString(1), SourceKind=reader.GetString(2),
                Status=reader.GetString(3), StorageRef=reader.GetString(4), Sha256=reader.GetString(5),
                MediaType=reader.GetString(6), FileName=reader.GetString(7), SizeBytes=reader.GetInt64(8),
                UploadedBy=reader.GetString(9), UploadedAt=reader.GetFieldValue<DateTimeOffset>(10),
                ReviewedBy=reader.IsDBNull(11)?null:reader.GetString(11),
                ReviewedAt=reader.IsDBNull(12)?null:reader.GetFieldValue<DateTimeOffset>(12),
                ExtractionStatus=reader.GetString(13), ExtractionError=reader.IsDBNull(14)?null:reader.GetString(14),
                ExtractorVersion=reader.IsDBNull(15)?null:reader.GetString(15)
            };
        }
        await using var context = _dataSource.CreateCommand(
            "SELECT dimension_code, dimension_value FROM knowledge_source_context WHERE source_id = @id ORDER BY dimension_code;");
        context.Parameters.AddWithValue("id", value.SourceId);
        await using var contextReader = await context.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await contextReader.ReadAsync(ct).ConfigureAwait(false))
            dimensions[contextReader.GetString(0)] = contextReader.GetString(1);
        return value with { ContextSelector = dimensions };
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadFragmentValuesAsync(
        Guid fragmentId,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT value_code, value_text FROM knowledge_fragment_values WHERE fragment_id = @id ORDER BY value_code;");
        command.Parameters.AddWithValue("id", fragmentId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values[reader.GetString(0)] = reader.GetString(1);
        return values;
    }

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法读取 {typeof(T).Name} 数据。");

    private static void AddJson<T>(NpgsqlCommand command, T value)
        => command.Parameters.Add(new NpgsqlParameter(
            "payload",
            NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions)
        });

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value)
        => command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private string GetRootPath()
        => Path.GetFullPath(_options.RootPath, AppContext.BaseDirectory);

    private string? GetArchiveRootPath()
        => string.IsNullOrWhiteSpace(_options.ArchiveRootPath)
            ? null
            : Path.GetFullPath(_options.ArchiveRootPath, AppContext.BaseDirectory);

    private string GetKnowledgePath(string sha256, string fileName)
        => Path.Combine(GetRootPath(), sha256[..2], sha256, fileName);

    private string? GetArchivePath(string sha256, string fileName)
        => GetArchiveRootPath() is { } root
            ? Path.Combine(root, sha256[..2], sha256, fileName)
            : null;

    private static async Task CopyToArchiveAsync(
        string sourcePath,
        string archivePath,
        CancellationToken ct)
    {
        if (File.Exists(archivePath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        var tempPath = $"{archivePath}.{Guid.CreateVersion7():N}.archiving";
        try
        {
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
                await target.FlushAsync(ct).ConfigureAwait(false);
            }
            if (!File.Exists(archivePath))
                File.Move(tempPath, archivePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
