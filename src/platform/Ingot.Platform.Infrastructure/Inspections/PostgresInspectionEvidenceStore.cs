using System.Security.Cryptography;
using Ingot.Contracts.Inspections;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class PostgresInspectionEvidenceStore : IInspectionEvidenceStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly InspectionEvidenceOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresInspectionEvidenceStore(
        IConfiguration configuration,
        IOptions<InspectionEvidenceOptions> options)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _options = options.Value;
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
            Directory.CreateDirectory(GetRootPath());
            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS inspection_evidence (
                  evidence_id UUID PRIMARY KEY,
                  storage_ref TEXT NOT NULL,
                  sha256 TEXT NOT NULL UNIQUE,
                  media_type TEXT NOT NULL,
                  file_name TEXT NOT NULL,
                  size_bytes BIGINT NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                  CHECK (size_bytes > 0)
                );
                CREATE INDEX IF NOT EXISTS idx_inspection_evidence_sha256
                  ON inspection_evidence(sha256);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<EvidenceUploadResponse> SaveAsync(
        Stream content,
        string fileName,
        string mediaType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await InitializeAsync(ct).ConfigureAwait(false);
        var safeFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 255)
            throw new ArgumentException("文件名不能为空且最长 255 个字符。", nameof(fileName));
        var normalizedMediaType = string.IsNullOrWhiteSpace(mediaType)
            ? "application/octet-stream"
            : mediaType.Trim().ToLowerInvariant();

        var tempPath = Path.Combine(GetRootPath(), $"{Guid.CreateVersion7():N}.uploading");
        long size = 0;
        string hash;
        using var sha = SHA256.Create();
        await using (var temp = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = await content.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                size += read;
                if (size > _options.MaxFileBytes)
                    throw new InvalidDataException($"证据文件超过 {_options.MaxFileBytes} 字节上限。");
                sha.TransformBlock(buffer, 0, read, null, 0);
                await temp.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            sha.TransformFinalBlock([], 0, 0);
            hash = Convert.ToHexStringLower(sha.Hash!);
        }

        if (size <= 0)
            throw new InvalidDataException("证据文件不能为空。");

        var finalPath = GetEvidencePath(hash, safeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (!File.Exists(finalPath))
            File.Move(tempPath, finalPath);
        else
            File.Delete(tempPath);
        var storageRef = $"evidence://sha256/{hash}/{Uri.EscapeDataString(safeFileName)}";

        await using var insert = _dataSource.CreateCommand(
            """
            INSERT INTO inspection_evidence(evidence_id, storage_ref, sha256, media_type, file_name, size_bytes)
            VALUES (@evidence_id, @storage_ref, @sha256, @media_type, @file_name, @size_bytes)
            ON CONFLICT (sha256) DO NOTHING
            RETURNING evidence_id;
            """);
        var evidenceId = Guid.CreateVersion7();
        insert.Parameters.AddWithValue("evidence_id", evidenceId);
        insert.Parameters.AddWithValue("storage_ref", storageRef);
        insert.Parameters.AddWithValue("sha256", hash);
        insert.Parameters.AddWithValue("media_type", normalizedMediaType);
        insert.Parameters.AddWithValue("file_name", safeFileName);
        insert.Parameters.AddWithValue("size_bytes", size);
        await insert.ExecuteScalarAsync(ct).ConfigureAwait(false);

        var stored = await GetByShaAsync(hash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("证据文件写入后无法读取元数据。");
        return new EvidenceUploadResponse
        {
            EvidenceId = stored.EvidenceId,
            StorageRef = stored.StorageRef,
            Sha256 = stored.Sha256,
            MediaType = stored.MediaType,
            FileName = stored.FileName,
            SizeBytes = stored.SizeBytes
        };
    }

    public async Task<InspectionEvidenceRef?> GetAsync(Guid evidenceId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT evidence_id, storage_ref, sha256, media_type, file_name, size_bytes
            FROM inspection_evidence
            WHERE evidence_id = @evidence_id;
            """);
        command.Parameters.AddWithValue("evidence_id", evidenceId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<bool> ExistsAsync(Guid evidenceId, CancellationToken ct = default)
        => await GetAsync(evidenceId, ct).ConfigureAwait(false) is not null;

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<InspectionEvidenceRef?> GetByShaAsync(string sha256, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT evidence_id, storage_ref, sha256, media_type, file_name, size_bytes
            FROM inspection_evidence
            WHERE sha256 = @sha256;
            """);
        command.Parameters.AddWithValue("sha256", sha256);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static InspectionEvidenceRef Read(NpgsqlDataReader reader)
        => new()
        {
            EvidenceId = reader.GetGuid(0),
            StorageRef = reader.GetString(1),
            Sha256 = reader.GetString(2),
            MediaType = reader.GetString(3),
            FileName = reader.GetString(4),
            SizeBytes = reader.GetInt64(5)
        };

    private string GetRootPath()
        => Path.GetFullPath(_options.RootPath, AppContext.BaseDirectory);

    private string GetEvidencePath(string sha256, string fileName)
        => Path.Combine(GetRootPath(), sha256[..2], sha256, fileName);
}
