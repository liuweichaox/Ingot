using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ResearchAssets;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class PostgresResearchAssetStore : IResearchAssetStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly ProcessKnowledgeOptions _options;

    public PostgresResearchAssetStore(
        IConfiguration configuration,
        IOptions<ProcessKnowledgeOptions> options)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
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

        await using var insert = _dataSource.CreateCommand(
            """
            INSERT INTO process_knowledge_sources(
              source_id, status, storage_ref, sha256, file_name, payload, updated_at)
            VALUES (@id, @status, @storage_ref, @sha256, @file_name, @payload, @updated_at)
            ON CONFLICT (sha256) DO NOTHING;
            """);
        insert.Parameters.AddWithValue("id", source.SourceId);
        insert.Parameters.AddWithValue("status", source.Status);
        insert.Parameters.AddWithValue("storage_ref", source.StorageRef);
        insert.Parameters.AddWithValue("sha256", source.Sha256);
        insert.Parameters.AddWithValue("file_name", source.FileName);
        AddJson(insert, source);
        insert.Parameters.AddWithValue("updated_at", now);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return await GetKnowledgeSourceByHashAsync(hash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("知识来源写入后无法读取。");
    }

    public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default)
        => GetByGuidAsync<KnowledgeSource>(
            "SELECT payload::text FROM process_knowledge_sources WHERE source_id = @id;",
            sourceId,
            ct);

    public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default)
        => ListAsync<KnowledgeSource>(
            "SELECT payload::text FROM process_knowledge_sources ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

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
            UPDATE process_knowledge_sources
            SET status = @status, payload = @payload, updated_at = @updated_at
            WHERE source_id = @id;
            """);
        command.Parameters.AddWithValue("id", value.SourceId);
        command.Parameters.AddWithValue("status", value.Status);
        AddJson(command, value);
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
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_knowledge_records(
              record_id, source_id, human_reviewed, payload, updated_at)
            VALUES (@id, @source_id, @human_reviewed, @payload, @updated_at)
            ON CONFLICT (record_id) DO UPDATE SET
              human_reviewed = EXCLUDED.human_reviewed,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.RecordId);
        command.Parameters.AddWithValue("source_id", value.SourceId);
        command.Parameters.AddWithValue("human_reviewed", value.HumanReviewed);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.ReviewedAt ?? value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(
        Guid sourceId,
        CancellationToken ct = default)
        => ListByGuidAsync<KnowledgeRecord>(
            """
            SELECT payload::text FROM process_knowledge_records
            WHERE source_id = @id ORDER BY updated_at DESC
            LIMIT 500;
            """,
            sourceId,
            ct);

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

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

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

    private Task<T?> GetByGuidAsync<T>(string sql, Guid id, CancellationToken ct)
        => GetSingleAsync<T>(
            sql,
            command => command.Parameters.AddWithValue("id", id),
            ct);

    private Task<IReadOnlyList<T>> ListByGuidAsync<T>(string sql, Guid id, CancellationToken ct)
        => ListAsync<T>(
            sql,
            command => command.Parameters.AddWithValue("id", id),
            ct);

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
        CancellationToken ct)
        => await GetSingleAsync<KnowledgeSource>(
            "SELECT payload::text FROM process_knowledge_sources WHERE sha256 = @sha256;",
            command => command.Parameters.AddWithValue("sha256", sha256),
            ct).ConfigureAwait(false);

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
