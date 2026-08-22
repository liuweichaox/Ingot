
// 将经签名校验的检验附件按站点去重并保存到文件与 Postgres 元数据。
using System.Security.Cryptography;
using Ingot.Contracts.Inspections;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class PostgresInspectionAttachmentStore : IInspectionAttachmentStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly InspectionAttachmentOptions _options;

    public PostgresInspectionAttachmentStore(
        NpgsqlDataSource dataSource,
        IOptions<InspectionAttachmentOptions> options)
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
    public async Task<AttachmentUploadResponse> SaveAsync(
        Stream content,
        string fileName,
        string mediaType,
        string siteId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await InitializeAsync(ct).ConfigureAwait(false);
        var safeFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 255)
            throw new ArgumentException("文件名不能为空且最长 255 个字符。", nameof(fileName));
        siteId = siteId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(siteId) || siteId.Length > 128)
            throw new ArgumentException("SiteId 不能为空且最长 128 个字符。", nameof(siteId));
        var tempPath = Path.Combine(GetRootPath(), $"{Guid.CreateVersion7():N}.uploading");
        long size = 0;
        string hash;
        var prefix = new byte[8];
        var prefixLength = 0;
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
                    throw new InvalidDataException($"附件超过 {_options.MaxFileBytes} 字节上限。");
                if (prefixLength < prefix.Length)
                {
                    var copyLength = Math.Min(read, prefix.Length - prefixLength);
                    buffer.AsSpan(0, copyLength).CopyTo(prefix.AsSpan(prefixLength));
                    prefixLength += copyLength;
                }
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
            throw new InvalidDataException("附件不能为空。");

        string normalizedMediaType;
        try
        {
            normalizedMediaType = InspectionAttachmentPolicy.DetectMediaType(
                safeFileName,
                prefix.AsSpan(0, prefixLength));
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
        var existing = await GetByShaAsync(siteId, hash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            File.Delete(tempPath);
            return new AttachmentUploadResponse
            {
                SiteId = existing.SiteId,
                AttachmentId = existing.AttachmentId,
                StorageRef = existing.StorageRef,
                Sha256 = existing.Sha256,
                MediaType = existing.MediaType,
                FileName = existing.FileName,
                SizeBytes = existing.SizeBytes
            };
        }

        var finalPath = GetAttachmentPath(hash, safeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (!File.Exists(finalPath))
            File.Move(tempPath, finalPath);
        else
            File.Delete(tempPath);
        if (GetArchivePath(hash, safeFileName) is { } archivePath)
            await CopyToArchiveAsync(finalPath, archivePath, ct).ConfigureAwait(false);
        var storageRef = $"attachment://sha256/{hash}/{Uri.EscapeDataString(safeFileName)}";

        await using var insert = _dataSource.CreateCommand(
            """
            INSERT INTO inspection_attachments(attachment_id, site_id, storage_ref, sha256, media_type, file_name, size_bytes)
            VALUES (@attachment_id, @site_id, @storage_ref, @sha256, @media_type, @file_name, @size_bytes)
            ON CONFLICT (site_id, sha256) DO NOTHING
            RETURNING attachment_id;
            """);
        var attachmentId = Guid.CreateVersion7();
        insert.Parameters.AddWithValue("attachment_id", attachmentId);
        insert.Parameters.AddWithValue("site_id", siteId);
        insert.Parameters.AddWithValue("storage_ref", storageRef);
        insert.Parameters.AddWithValue("sha256", hash);
        insert.Parameters.AddWithValue("media_type", normalizedMediaType);
        insert.Parameters.AddWithValue("file_name", safeFileName);
        insert.Parameters.AddWithValue("size_bytes", size);
        await insert.ExecuteScalarAsync(ct).ConfigureAwait(false);

        var stored = await GetByShaAsync(siteId, hash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("附件写入后无法读取元数据。");
        if (!string.Equals(stored.StorageRef, storageRef, StringComparison.Ordinal))
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            if (GetArchivePath(hash, safeFileName) is { } duplicateArchivePath && File.Exists(duplicateArchivePath))
                File.Delete(duplicateArchivePath);
        }
        return new AttachmentUploadResponse
        {
            SiteId = stored.SiteId,
            AttachmentId = stored.AttachmentId,
            StorageRef = stored.StorageRef,
            Sha256 = stored.Sha256,
            MediaType = stored.MediaType,
            FileName = stored.FileName,
            SizeBytes = stored.SizeBytes
        };
    }

    public async Task<InspectionAttachment?> GetAsync(Guid attachmentId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            SELECT attachment_id, site_id, storage_ref, sha256, media_type, file_name, size_bytes
            FROM inspection_attachments
            WHERE attachment_id = @attachment_id;
            """);
        command.Parameters.AddWithValue("attachment_id", attachmentId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<bool> ExistsAsync(Guid attachmentId, CancellationToken ct = default)
        => await GetAsync(attachmentId, ct).ConfigureAwait(false) is not null;

    public async Task<Stream?> OpenReadAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await GetAsync(attachmentId, ct).ConfigureAwait(false);
        if (attachment is null)
            return null;
        var path = GetAttachmentPath(attachment.Sha256, attachment.FileName);
        if (!File.Exists(path) && GetArchivePath(attachment.Sha256, attachment.FileName) is { } archivePath)
            path = archivePath;
        if (!File.Exists(path))
            return null;
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private async Task<InspectionAttachment?> GetByShaAsync(
        string siteId,
        string sha256,
        CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT attachment_id, site_id, storage_ref, sha256, media_type, file_name, size_bytes
            FROM inspection_attachments
            WHERE site_id = @site_id AND sha256 = @sha256;
            """);
        command.Parameters.AddWithValue("sha256", sha256);
        command.Parameters.AddWithValue("site_id", siteId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    private static InspectionAttachment Read(NpgsqlDataReader reader)
        => new()
        {
            AttachmentId = reader.GetGuid(0),
            SiteId = reader.GetString(1),
            StorageRef = reader.GetString(2),
            Sha256 = reader.GetString(3),
            MediaType = reader.GetString(4),
            FileName = reader.GetString(5),
            SizeBytes = reader.GetInt64(6)
        };

    private string GetRootPath()
        => Path.GetFullPath(_options.RootPath, AppContext.BaseDirectory);

    private string? GetArchiveRootPath()
        => string.IsNullOrWhiteSpace(_options.ArchiveRootPath)
            ? null
            : Path.GetFullPath(_options.ArchiveRootPath, AppContext.BaseDirectory);

    private string GetAttachmentPath(string sha256, string fileName)
        => Path.Combine(GetRootPath(), sha256[..2], sha256, fileName);

    private string? GetArchivePath(string sha256, string fileName)
        => GetArchiveRootPath() is { } root
            ? Path.Combine(root, sha256[..2], sha256, fileName)
            : null;

    private static async Task CopyToArchiveAsync(string sourcePath, string archivePath, CancellationToken ct)
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
