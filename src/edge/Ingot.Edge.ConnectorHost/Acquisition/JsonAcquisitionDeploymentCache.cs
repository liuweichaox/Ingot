using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal interface IAcquisitionDeploymentCache
{
    Task<IReadOnlyList<AcquisitionDeployment>?> LoadAsync(
        string edgeId,
        CancellationToken ct = default);

    Task SaveAsync(
        string edgeId,
        IReadOnlyList<AcquisitionDeployment> deployments,
        CancellationToken ct = default);
}

/// <summary>
///     保存平台最后一次成功下发的不可变采集部署。缓存只属于一个 EdgeId，
///     避免复制磁盘或修改启动身份后误用其他节点的设备配置。
/// </summary>
internal sealed class JsonAcquisitionDeploymentCache(
    IOptions<HttpPollingAcquisitionOptions> options,
    ILogger<JsonAcquisitionDeploymentCache> logger) : IAcquisitionDeploymentCache
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path = ResolvePath(options.Value.DeploymentCachePath);

    public async Task<IReadOnlyList<AcquisitionDeployment>?> LoadAsync(
        string edgeId,
        CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return null;
        try
        {
            await using var stream = File.OpenRead(_path);
            var cached = await JsonSerializer.DeserializeAsync<CachedDeployments>(
                stream,
                JsonOptions,
                ct).ConfigureAwait(false);
            if (cached is null ||
                cached.SchemaVersion != SchemaVersion ||
                !string.Equals(cached.EdgeId, edgeId, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "忽略不属于当前 Edge 或版本不兼容的采集配置缓存：Path={Path}, EdgeId={EdgeId}",
                    _path,
                    edgeId);
                return null;
            }
            if (cached.Deployments.Any(deployment =>
                    !string.Equals(deployment.Task.EdgeId, edgeId, StringComparison.Ordinal)))
            {
                logger.LogWarning(
                    "忽略包含其他 Edge 配置的采集缓存：Path={Path}, EdgeId={EdgeId}",
                    _path,
                    edgeId);
                return null;
            }
            return cached.Deployments;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(exception, "读取采集配置缓存失败：Path={Path}", _path);
            return null;
        }
    }

    public async Task SaveAsync(
        string edgeId,
        IReadOnlyList<AcquisitionDeployment> deployments,
        CancellationToken ct = default)
    {
        if (deployments.Any(deployment =>
                !string.Equals(deployment.Task.EdgeId, edgeId, StringComparison.Ordinal)))
            throw new InvalidOperationException("不能把其他 Edge 的采集配置写入当前节点缓存。");

        var existing = await LoadAsync(edgeId, ct).ConfigureAwait(false);
        if (existing is not null &&
            JsonSerializer.SerializeToUtf8Bytes(existing, JsonOptions)
                .AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(deployments, JsonOptions)))
            return;

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("采集配置缓存路径缺少父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new CachedDeployments(
                        SchemaVersion,
                        edgeId,
                        DateTimeOffset.UtcNow,
                        deployments),
                    JsonOptions,
                    ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException("Acquisition:DeploymentCachePath 不能为空。");
        return Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
    }

    private sealed record CachedDeployments(
        int SchemaVersion,
        string EdgeId,
        DateTimeOffset SavedAt,
        IReadOnlyList<AcquisitionDeployment> Deployments);
}
