using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;

namespace Ingot.Connector.Builder;

public sealed partial class FileConnectorWorkspaceService(
    IConnectorCommandRunner runner,
    IOptions<ConnectorBuilderOptions> options) : IConnectorWorkspaceService
{
    private const string MetadataDirectory = ".ingot";
    private const string MetadataFile = ".ingot/workspace.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false
    };
    private readonly ConnectorBuilderOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _workspaceLocks = new(StringComparer.Ordinal);

    public async Task<ConnectorWorkspaceSnapshot> CreateAsync(
        string actorId,
        string runId,
        string specificationArtifactId,
        string packageName,
        CancellationToken ct = default)
    {
        var normalizedPackage = NormalizePackageName(packageName);
        var workspaceId = Guid.CreateVersion7().ToString("N");
        var root = GetWorkspacePath(workspaceId);
        Directory.CreateDirectory(Path.Combine(root, MetadataDirectory));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "tests", "fixtures"));

        var snapshot = new ConnectorWorkspaceSnapshot
        {
            WorkspaceId = workspaceId,
            ActorId = Required(actorId, nameof(actorId)),
            RunId = Required(runId, nameof(runId)),
            SpecificationArtifactId = Required(specificationArtifactId, nameof(specificationArtifactId)),
            PackageName = normalizedPackage,
            Status = ConnectorWorkspaceStatuses.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteTemplateAsync(root, normalizedPackage, ct).ConfigureAwait(false);
        await SaveAsync(root, snapshot, ct).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<ConnectorWorkspaceSnapshot?> GetAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        var root = GetWorkspacePath(workspaceId);
        var metadata = Path.Combine(root, MetadataFile);
        if (!File.Exists(metadata))
            return null;
        await using var stream = new FileStream(
            metadata,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await JsonSerializer.DeserializeAsync<ConnectorWorkspaceSnapshot>(stream, JsonOptions, ct)
            .ConfigureAwait(false);
        if (snapshot is null || !string.Equals(snapshot.ActorId, actorId, StringComparison.Ordinal))
            return null;
        return snapshot;
    }

    public async Task<IReadOnlyList<string>> ListFilesAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        var (_, root) = await RequiredWorkspaceAsync(actorId, workspaceId, ct).ConfigureAwait(false);
        return Directory.EnumerateFiles(root, "*", SafeEnumeration)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !IsInternalOrGenerated(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<string> ReadFileAsync(
        string actorId,
        string workspaceId,
        string relativePath,
        CancellationToken ct = default)
    {
        var (_, root) = await RequiredWorkspaceAsync(actorId, workspaceId, ct).ConfigureAwait(false);
        var path = ResolveUserPath(root, relativePath);
        EnsureNoSymbolicLinks(root, path);
        if (!File.Exists(path))
            throw new FileNotFoundException("工作区文件不存在。", relativePath);
        var info = new FileInfo(path);
        if (info.Length > Math.Clamp(_options.MaxFileBytes, 1_024, 4 * 1024 * 1024))
            throw new InvalidOperationException("工作区文件超过读取上限。 ");
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    public async Task<ConnectorWorkspaceSnapshot> WriteFileAsync(
        string actorId,
        string workspaceId,
        string relativePath,
        string content,
        CancellationToken ct = default)
    {
        using var workspaceLock = await AcquireWorkspaceLockAsync(workspaceId, ct).ConfigureAwait(false);
        var (snapshot, root) = await RequiredWorkspaceAsync(actorId, workspaceId, ct).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetByteCount(content);
        if (bytes > Math.Clamp(_options.MaxFileBytes, 1_024, 4 * 1024 * 1024))
            throw new ArgumentException("单个工作区文件超过写入上限。", nameof(content));
        var path = ResolveUserPath(root, relativePath);
        EnsureNoSymbolicLinks(root, path);
        EnsureWorkspaceCapacity(root, path, bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stagingRoot = Path.Combine(root, MetadataDirectory, "staging");
        Directory.CreateDirectory(stagingRoot);
        var temporary = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), ct).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        var updated = snapshot with
        {
            Status = ConnectorWorkspaceStatuses.Ready,
            Revision = snapshot.Revision + 1,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastBuild = null,
            LastTest = null,
            PackageSha256 = null,
            PackageApprovedBy = null,
            PackageApprovedAt = null
        };
        await SaveAsync(root, updated, CancellationToken.None).ConfigureAwait(false);
        return updated;
    }

    public async Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorCommandResult Result)> BuildAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        using var workspaceLock = await AcquireWorkspaceLockAsync(workspaceId, ct).ConfigureAwait(false);
        var current = await RequiredWorkspaceAsync(actorId, workspaceId, ct).ConfigureAwait(false);
        return await RunLockedAsync(current.Snapshot, current.Root, "build", ct).ConfigureAwait(false);
    }

    public async Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorCommandResult Result)> TestAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        using var workspaceLock = await AcquireWorkspaceLockAsync(workspaceId, ct).ConfigureAwait(false);
        var current = await GetAsync(actorId, workspaceId, ct).ConfigureAwait(false)
                      ?? throw new KeyNotFoundException("连接器工作区不存在或不属于当前操作者。 ");
        if (current.Status != ConnectorWorkspaceStatuses.Built || current.LastBuild?.Succeeded != true)
            throw new InvalidOperationException("连接器必须先成功构建，才能运行测试。 ");
        return await RunLockedAsync(current, GetWorkspacePath(workspaceId), "test", ct).ConfigureAwait(false);
    }

    public async Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorPackageDescriptor Package)> PackageAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        using var workspaceLock = await AcquireWorkspaceLockAsync(workspaceId, ct).ConfigureAwait(false);
        var (snapshot, root) = await RequiredWorkspaceAsync(actorId, workspaceId, ct).ConfigureAwait(false);
        if (snapshot.Status == ConnectorWorkspaceStatuses.Packaged)
            return (snapshot, await ReadPackageDescriptorAsync(snapshot, ct).ConfigureAwait(false));
        if (snapshot.LastTest?.Succeeded != true)
            throw new InvalidOperationException("只有测试通过的连接器才能打包。 ");
        if (snapshot.Status != ConnectorWorkspaceStatuses.PackageApproved || snapshot.PackageApprovedAt is null)
            throw new InvalidOperationException("连接器必须由操作者显式批准后才能打包。 ");

        var artifactRoot = Path.GetFullPath(_options.ArtifactRoot);
        Directory.CreateDirectory(artifactRoot);
        var temporary = Path.Combine(artifactRoot, $"{workspaceId}.tmp.zip");
        if (File.Exists(temporary))
            File.Delete(temporary);
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SafeEnumeration)
                         .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsInternalOrGenerated(relative))
                    continue;
                archive.CreateEntryFromFile(file, relative, CompressionLevel.SmallestSize);
            }
        }

        var hash = await Sha256Async(temporary, ct).ConfigureAwait(false);
        var finalName = $"{snapshot.PackageName}-{hash}.zip";
        var finalPath = Path.Combine(artifactRoot, finalName);
        try
        {
            File.Move(temporary, finalPath);
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            var existingHash = await Sha256Async(finalPath, ct).ConfigureAwait(false);
            if (!string.Equals(existingHash, hash, StringComparison.Ordinal))
                throw new InvalidOperationException("内容寻址的连接器包发生哈希冲突。 ");
            File.Delete(temporary);
        }
        var package = new ConnectorPackageDescriptor
        {
            WorkspaceId = workspaceId,
            PackageName = snapshot.PackageName,
            Sha256 = hash,
            SizeBytes = new FileInfo(finalPath).Length,
            RelativePath = finalName,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var updated = snapshot with
        {
            Status = ConnectorWorkspaceStatuses.Packaged,
            PackageSha256 = hash,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveAsync(root, updated, CancellationToken.None).ConfigureAwait(false);
        return (updated, package);
    }

    public async Task<ConnectorPackageDownload> OpenPackageAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        var snapshot = await GetAsync(actorId, workspaceId, ct).ConfigureAwait(false)
                       ?? throw new KeyNotFoundException("连接器工作区不存在或不属于当前操作者。 ");
        if (snapshot.Status != ConnectorWorkspaceStatuses.Packaged ||
            snapshot.PackageSha256 is null ||
            !Sha256Regex().IsMatch(snapshot.PackageSha256))
        {
            throw new InvalidOperationException("连接器尚未生成可下载的打包制品。 ");
        }

        var fileName = $"{snapshot.PackageName}-{snapshot.PackageSha256}.zip";
        var artifactRoot = Path.GetFullPath(_options.ArtifactRoot);
        var path = Path.GetFullPath(Path.Combine(artifactRoot, fileName));
        if (!path.StartsWith(artifactRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            throw new FileNotFoundException("连接器打包制品不存在。", fileName);
        var actualHash = await Sha256Async(path, ct).ConfigureAwait(false);
        if (!string.Equals(actualHash, snapshot.PackageSha256, StringComparison.Ordinal))
            throw new InvalidDataException("连接器打包制品内容与记录的 SHA-256 不一致。 ");

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new ConnectorPackageDownload
        {
            Content = stream,
            FileName = fileName,
            Sha256 = snapshot.PackageSha256,
            SizeBytes = stream.Length
        };
    }

    private async Task<ConnectorPackageDescriptor> ReadPackageDescriptorAsync(
        ConnectorWorkspaceSnapshot snapshot,
        CancellationToken ct)
    {
        if (snapshot.PackageSha256 is null || !Sha256Regex().IsMatch(snapshot.PackageSha256))
            throw new InvalidOperationException("连接器打包元数据不完整。 ");
        var fileName = $"{snapshot.PackageName}-{snapshot.PackageSha256}.zip";
        var artifactRoot = Path.GetFullPath(_options.ArtifactRoot);
        var path = Path.GetFullPath(Path.Combine(artifactRoot, fileName));
        if (!path.StartsWith(artifactRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            throw new FileNotFoundException("连接器打包制品不存在。", fileName);
        var actualHash = await Sha256Async(path, ct).ConfigureAwait(false);
        if (!string.Equals(actualHash, snapshot.PackageSha256, StringComparison.Ordinal))
            throw new InvalidDataException("连接器打包制品内容与记录的 SHA-256 不一致。 ");
        var info = new FileInfo(path);
        return new ConnectorPackageDescriptor
        {
            WorkspaceId = snapshot.WorkspaceId,
            PackageName = snapshot.PackageName,
            Sha256 = snapshot.PackageSha256,
            SizeBytes = info.Length,
            RelativePath = fileName,
            CreatedAt = info.LastWriteTimeUtc
        };
    }

    public async Task<ConnectorWorkspaceSnapshot> ApprovePackagingAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default)
    {
        using var workspaceLock = await AcquireWorkspaceLockAsync(workspaceId, ct).ConfigureAwait(false);
        var (snapshot, root) = await RequiredWorkspaceAsync(actorId, workspaceId, ct).ConfigureAwait(false);
        if (snapshot.Status != ConnectorWorkspaceStatuses.AwaitingPackageApproval || snapshot.LastTest?.Succeeded != true)
            throw new InvalidOperationException("只有测试通过的连接器才能批准打包。 ");
        var updated = snapshot with
        {
            Status = ConnectorWorkspaceStatuses.PackageApproved,
            PackageApprovedBy = actorId,
            PackageApprovedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await SaveAsync(root, updated, CancellationToken.None).ConfigureAwait(false);
        return updated;
    }

    private async Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorCommandResult Result)> RunLockedAsync(
        ConnectorWorkspaceSnapshot snapshot,
        string root,
        string operation,
        CancellationToken ct)
    {
        var running = snapshot with
        {
            Status = operation == "build" ? ConnectorWorkspaceStatuses.Building : ConnectorWorkspaceStatuses.Testing,
            UpdatedAt = DateTimeOffset.UtcNow,
            PackageSha256 = operation == "build" ? null : snapshot.PackageSha256,
            PackageApprovedBy = operation == "build" ? null : snapshot.PackageApprovedBy,
            PackageApprovedAt = operation == "build" ? null : snapshot.PackageApprovedAt
        };
        await SaveAsync(root, running, ct).ConfigureAwait(false);
        ConnectorCommandResult result;
        try
        {
            result = await runner.RunAsync(root, operation, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SaveCommandFailureAsync(root, running, operation, "Command cancelled.").ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await SaveCommandFailureAsync(root, running, operation, exception.Message).ConfigureAwait(false);
            throw;
        }
        var updated = operation == "build"
            ? running with
            {
                Status = result.Succeeded ? ConnectorWorkspaceStatuses.Built : ConnectorWorkspaceStatuses.BuildFailed,
                LastBuild = result,
                LastTest = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }
            : running with
            {
                Status = result.Succeeded
                    ? ConnectorWorkspaceStatuses.AwaitingPackageApproval
                    : ConnectorWorkspaceStatuses.TestFailed,
                LastTest = result,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        await SaveAsync(root, updated, CancellationToken.None).ConfigureAwait(false);
        return (updated, result);
    }

    private static Task SaveCommandFailureAsync(
        string root,
        ConnectorWorkspaceSnapshot running,
        string operation,
        string output)
    {
        var result = new ConnectorCommandResult
        {
            Operation = operation,
            Succeeded = false,
            ExitCode = -1,
            DurationMilliseconds = 0,
            Output = output,
            CompletedAt = DateTimeOffset.UtcNow
        };
        var failed = operation == "build"
            ? running with
            {
                Status = ConnectorWorkspaceStatuses.BuildFailed,
                LastBuild = result,
                LastTest = null,
                UpdatedAt = DateTimeOffset.UtcNow
            }
            : running with
            {
                Status = ConnectorWorkspaceStatuses.TestFailed,
                LastTest = result,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        return SaveAsync(root, failed, CancellationToken.None);
    }

    private async Task<IDisposable> AcquireWorkspaceLockAsync(string workspaceId, CancellationToken ct)
    {
        if (!WorkspaceIdRegex().IsMatch(workspaceId))
            throw new ArgumentException("WorkspaceId 格式不合法。", nameof(workspaceId));
        var gate = _workspaceLocks.GetOrAdd(workspaceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new WorkspaceLockLease(gate);
    }

    private async Task<(ConnectorWorkspaceSnapshot Snapshot, string Root)> RequiredWorkspaceAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct)
    {
        var snapshot = await GetAsync(actorId, workspaceId, ct).ConfigureAwait(false)
                       ?? throw new KeyNotFoundException("连接器工作区不存在或不属于当前操作者。 ");
        return (snapshot, GetWorkspacePath(workspaceId));
    }

    private string GetWorkspacePath(string workspaceId)
    {
        if (!WorkspaceIdRegex().IsMatch(workspaceId))
            throw new ArgumentException("WorkspaceId 格式不合法。", nameof(workspaceId));
        var basePath = Path.GetFullPath(_options.WorkspaceRoot);
        var path = Path.GetFullPath(Path.Combine(basePath, workspaceId));
        if (!path.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("工作区路径越界。 ");
        return path;
    }

    private static string ResolveUserPath(string root, string relativePath)
    {
        var normalized = relativePath.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(part => part is "" or "." or "..") || IsInternalOrGenerated(normalized))
            throw new UnauthorizedAccessException("文件路径不在可写工作区范围内。 ");
        var path = Path.GetFullPath(Path.Combine(root, normalized));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("文件路径越界。 ");
        return path;
    }

    private static void EnsureNoSymbolicLinks(string root, string path)
    {
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, path)
                     .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var file = new FileInfo(current);
            file.Refresh();
            if (file.LinkTarget is not null)
                throw new UnauthorizedAccessException("工作区路径不能包含符号链接。 ");
            var directory = new DirectoryInfo(current);
            directory.Refresh();
            if (directory.LinkTarget is not null)
                throw new UnauthorizedAccessException("工作区路径不能包含符号链接。 ");
        }
    }

    private void EnsureWorkspaceCapacity(string root, string targetPath, int replacementBytes)
    {
        var files = Directory.EnumerateFiles(root, "*", SafeEnumeration)
            .Where(path => !IsInternalOrGenerated(Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Select(path => new FileInfo(path))
            .ToArray();
        var existing = files.FirstOrDefault(file => string.Equals(file.FullName, targetPath, StringComparison.Ordinal));
        var nextFileCount = files.Length + (existing is null ? 1 : 0);
        var maxFiles = Math.Clamp(_options.MaxWorkspaceFiles, 16, 2_048);
        if (nextFileCount > maxFiles)
            throw new InvalidOperationException($"连接器工作区文件数不得超过 {maxFiles} 个。");

        var nextBytes = files.Sum(static file => file.Length) - (existing?.Length ?? 0) + replacementBytes;
        var maxBytes = Math.Clamp(_options.MaxWorkspaceBytes, 1024 * 1024, 64L * 1024 * 1024);
        if (nextBytes > maxBytes)
            throw new InvalidOperationException($"连接器工作区源码总量不得超过 {maxBytes} 字节。");
    }

    private static bool IsInternalOrGenerated(string relativePath)
    {
        var first = relativePath.Replace('\\', '/').Split('/')[0];
        return string.Equals(first, MetadataDirectory, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(first, ".git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(first, "bin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(first, "obj", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SaveAsync(string root, ConnectorWorkspaceSnapshot snapshot, CancellationToken ct)
    {
        var path = Path.Combine(root, MetadataFile);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, JsonOptions), ct).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task WriteTemplateAsync(string root, string packageName, CancellationToken ct)
    {
        var namespaceName = "Connector_" + packageName.Replace("-", "_", StringComparison.Ordinal);
        var files = new Dictionary<string, string>
        {
            ["Connector.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <AssemblyName>Ingot.Connector</AssemblyName>
                  </PropertyGroup>
                </Project>
                """,
            ["NuGet.Config"] = """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                  </packageSources>
                </configuration>
                """,
            ["src/GeneratedConnector.cs"] = $$"""
                using System.Text.Json;

                namespace Ingot.Generated.{{namespaceName}};

                public static class GeneratedConnector
                {
                    public static string Map(string sourceJson)
                    {
                        using var document = JsonDocument.Parse(sourceJson);
                        var now = DateTimeOffset.UtcNow;
                        return JsonSerializer.Serialize(new
                        {
                            eventId = Guid.CreateVersion7().ToString(),
                            eventType = "source.observed",
                            eventTypeVersion = 1,
                            occurredAt = now,
                            recordedAt = now,
                            source = "connector/{{packageName}}",
                            subject = new { type = "source", id = "{{packageName}}" },
                            context = new Dictionary<string, string>(),
                            data = new { payload = document.RootElement.Clone() },
                            correlationId = (string?)null,
                            seq = 0L
                        });
                    }
                }
                """,
            ["src/Program.cs"] = $$"""
                using Ingot.Generated.{{namespaceName}};

                while (Console.ReadLine() is { } sourceJson)
                {
                    try
                    {
                        Console.WriteLine(GeneratedConnector.Map(sourceJson));
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(exception.Message);
                        Environment.ExitCode = 1;
                    }
                }
                """,
            ["tests/Connector.Tests.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Connector.csproj" />
                  </ItemGroup>
                </Project>
                """,
            ["tests/Program.cs"] = $$"""
                using Ingot.Generated.{{namespaceName}};
                using System.Text.Json;

                var output = GeneratedConnector.Map("{\"value\":1}");
                using var document = JsonDocument.Parse(output);
                var root = document.RootElement;
                var required = new[]
                {
                    "eventId", "eventType", "eventTypeVersion", "occurredAt", "recordedAt", "source",
                    "subject", "context", "data", "correlationId", "seq"
                };
                if (required.Any(name => !root.TryGetProperty(name, out _)) ||
                    !Guid.TryParse(root.GetProperty("eventId").GetString(), out var eventId) ||
                    eventId.Version != 7 ||
                    root.GetProperty("eventType").GetString() != "source.observed" ||
                    root.GetProperty("eventTypeVersion").GetInt32() != 1 ||
                    root.GetProperty("seq").GetInt64() != 0 ||
                    root.GetProperty("subject").GetProperty("id").GetString() != "{{packageName}}" ||
                    !root.GetProperty("data").TryGetProperty("payload", out _))
                {
                    throw new InvalidOperationException("Connector fixture did not emit a valid ProductionEvent envelope.");
                }
                Console.WriteLine("Connector fixture passed.");
                """,
            ["tests/fixtures/sample.json"] = "{\"value\":1}",
            ["connector.manifest.json"] = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                name = packageName,
                runtime = "dotnet-10",
                entrypoint = new[] { "dotnet", "Ingot.Connector.dll" },
                input = new { transport = "stdin", format = "json-lines" },
                output = new { transport = "stdout", format = "production-event-json-lines" },
                permissions = new { deviceAccess = "read-only", network = Array.Empty<string>() }
            }, JsonOptions),
            ["Dockerfile"] = """
                FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
                WORKDIR /src
                COPY Connector.csproj NuGet.Config ./
                COPY src/ ./src/
                RUN dotnet publish Connector.csproj -c Release -o /app/publish --nologo

                FROM mcr.microsoft.com/dotnet/runtime:10.0
                WORKDIR /app
                COPY --from=build --chown=65532:65532 /app/publish/ ./
                USER 65532:65532
                ENTRYPOINT ["dotnet", "Ingot.Connector.dll"]
                """,
            [".dockerignore"] = """
                **
                !Connector.csproj
                !NuGet.Config
                !src/
                !src/**
                """
        };
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static string NormalizePackageName(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!PackageNameRegex().IsMatch(normalized))
            throw new ArgumentException("PackageName 只允许小写字母、数字和单个连字符。", nameof(value));
        return normalized;
    }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("值不能为空。", name) : value.Trim();

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceIdRegex();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageNameRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    private sealed class WorkspaceLockLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
