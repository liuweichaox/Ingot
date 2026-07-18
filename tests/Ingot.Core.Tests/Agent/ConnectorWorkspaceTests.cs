using Ingot.Connector.Builder;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class ConnectorWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ingot-builder-{Guid.NewGuid():N}");

    [Fact]
    public async Task Workspace_WritesBuildsTestsApprovesAndPackages()
    {
        var service = CreateService(new SuccessfulRunner());
        var workspace = await service.CreateAsync("engineer-a", "run-1", "spec-1", "furnace-http");

        var generatedSource = await service.ReadFileAsync(
            "engineer-a", workspace.WorkspaceId, "src/GeneratedConnector.cs");
        foreach (var property in new[]
                 {
                     "eventId", "eventType", "eventTypeVersion", "occurredAt", "recordedAt", "source",
                     "subject", "context", "data", "correlationId", "seq"
                 })
        {
            Assert.Contains(property, generatedSource, StringComparison.Ordinal);
        }

        workspace = await service.WriteFileAsync(
            "engineer-a", workspace.WorkspaceId, "src/ConnectorNotes.cs", "// Agent-authored connector revision.");
        Assert.Equal(1, workspace.Revision);
        Assert.Contains("src/ConnectorNotes.cs", await service.ListFilesAsync("engineer-a", workspace.WorkspaceId));

        var build = await service.BuildAsync("engineer-a", workspace.WorkspaceId);
        Assert.True(build.Result.Succeeded);
        var test = await service.TestAsync("engineer-a", workspace.WorkspaceId);
        Assert.True(test.Result.Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PackageAsync("engineer-a", workspace.WorkspaceId));

        var approved = await service.ApprovePackagingAsync("engineer-a", workspace.WorkspaceId);
        Assert.Equal("engineer-a", approved.PackageApprovedBy);
        Assert.Equal(ConnectorWorkspaceStatuses.PackageApproved, approved.Status);
        var packaged = await service.PackageAsync("engineer-a", workspace.WorkspaceId);
        Assert.Equal(ConnectorWorkspaceStatuses.Packaged, packaged.Workspace.Status);
        Assert.Equal(64, packaged.Package.Sha256.Length);
        var download = await service.OpenPackageAsync("engineer-a", workspace.WorkspaceId);
        await using var packageStream = download.Content;
        Assert.Equal(packaged.Package.RelativePath, download.FileName);
        Assert.Equal(packaged.Package.Sha256, download.Sha256);
        Assert.Equal(packaged.Package.SizeBytes, download.SizeBytes);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Contains(archive.Entries, entry => entry.FullName == "src/Program.cs");
        var dockerfile = archive.GetEntry("Dockerfile");
        Assert.NotNull(dockerfile);
        using var reader = new StreamReader(dockerfile!.Open());
        var dockerfileContent = await reader.ReadToEndAsync();
        Assert.Contains("dotnet publish Connector.csproj", dockerfileContent);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Ingot.Connector.dll\"]", dockerfileContent);
        var manifest = archive.GetEntry("connector.manifest.json");
        Assert.NotNull(manifest);
        using var manifestReader = new StreamReader(manifest!.Open());
        var manifestContent = await manifestReader.ReadToEndAsync();
        Assert.Contains("production-event-json-lines", manifestContent);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.OpenPackageAsync("engineer-b", workspace.WorkspaceId));
    }

    [Fact]
    public async Task Workspace_PackagingIsRetryableAndDownloadRejectsTampering()
    {
        var service = CreateService(new SuccessfulRunner());
        var workspace = await service.CreateAsync("engineer-a", "run-package", "spec-package", "verified-package");
        await service.BuildAsync("engineer-a", workspace.WorkspaceId);
        await service.TestAsync("engineer-a", workspace.WorkspaceId);
        await service.ApprovePackagingAsync("engineer-a", workspace.WorkspaceId);
        var first = await service.PackageAsync("engineer-a", workspace.WorkspaceId);
        var retry = await service.PackageAsync("engineer-a", workspace.WorkspaceId);
        Assert.Equal(first.Package.Sha256, retry.Package.Sha256);

        var packagePath = Path.Combine(_root, "packages", first.Package.RelativePath);
        await File.AppendAllTextAsync(packagePath, "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.OpenPackageAsync("engineer-a", workspace.WorkspaceId));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("/tmp/secret.txt")]
    [InlineData(".ingot/workspace.json")]
    [InlineData(".INGOT/workspace.json")]
    [InlineData(".GIT/config")]
    [InlineData("obj/injected.cs")]
    [InlineData("OBJ/injected.cs")]
    [InlineData("src/BIN/injected.dll")]
    public async Task Workspace_RejectsPathsOutsideWritableSource(string path)
    {
        var service = CreateService(new SuccessfulRunner());
        var workspace = await service.CreateAsync("engineer-a", "run-2", "spec-2", "safe-package");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.WriteFileAsync("engineer-a", workspace.WorkspaceId, path, "unsafe"));
    }

    [Fact]
    public async Task Workspace_IsActorScoped()
    {
        var service = CreateService(new SuccessfulRunner());
        var workspace = await service.CreateAsync("engineer-a", "run-3", "spec-3", "private-package");
        Assert.Null(await service.GetAsync("engineer-b", workspace.WorkspaceId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ReadFileAsync("engineer-b", workspace.WorkspaceId, "Connector.csproj"));
    }

    [Fact]
    public async Task Workspace_RejectsSymbolicLinkReadsAndWrites()
    {
        var service = CreateService(new SuccessfulRunner());
        var workspace = await service.CreateAsync("engineer-a", "run-link", "spec-link", "link-package");
        var outside = Path.Combine(_root, "outside-secret.txt");
        await File.WriteAllTextAsync(outside, "secret");
        var link = Path.Combine(_root, "workspaces", workspace.WorkspaceId, "src", "linked.cs");
        File.CreateSymbolicLink(link, outside);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ReadFileAsync("engineer-a", workspace.WorkspaceId, "src/linked.cs"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.WriteFileAsync("engineer-a", workspace.WorkspaceId, "src/linked.cs", "overwrite"));
        Assert.DoesNotContain("src/linked.cs", await service.ListFilesAsync("engineer-a", workspace.WorkspaceId));
    }

    [Fact]
    public async Task Workspace_RequiresApprovalForTheCurrentRevision()
    {
        var service = CreateService(new SuccessfulRunner());
        var workspace = await service.CreateAsync("engineer-a", "run-4", "spec-4", "revision-package");
        await service.BuildAsync("engineer-a", workspace.WorkspaceId);
        await service.TestAsync("engineer-a", workspace.WorkspaceId);
        await service.ApprovePackagingAsync("engineer-a", workspace.WorkspaceId);

        var changed = await service.WriteFileAsync(
            "engineer-a", workspace.WorkspaceId, "src/Revision.cs", "// revision 2");

        Assert.Null(changed.PackageApprovedBy);
        Assert.Null(changed.PackageApprovedAt);
        await service.BuildAsync("engineer-a", workspace.WorkspaceId);
        var tested = await service.TestAsync("engineer-a", workspace.WorkspaceId);
        Assert.Equal(ConnectorWorkspaceStatuses.AwaitingPackageApproval, tested.Workspace.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PackageAsync("engineer-a", workspace.WorkspaceId));
    }

    [Fact]
    public async Task Workspace_SerializesBuildAndSourceWrites()
    {
        var runner = new BlockingRunner();
        var service = CreateService(runner);
        var workspace = await service.CreateAsync("engineer-a", "run-5", "spec-5", "serialized-package");

        var build = service.BuildAsync("engineer-a", workspace.WorkspaceId);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var write = service.WriteFileAsync(
            "engineer-a", workspace.WorkspaceId, "src/AfterBuild.cs", "// serialized");

        Assert.False(write.IsCompleted);
        runner.Release.TrySetResult();
        await build;
        var written = await write;
        Assert.Equal(ConnectorWorkspaceStatuses.Ready, written.Status);
        Assert.Null(written.LastBuild);
    }

    [Fact]
    public async Task Workspace_PersistsFailedStateWhenBuildIsCancelled()
    {
        var runner = new BlockingRunner();
        var service = CreateService(runner);
        var workspace = await service.CreateAsync("engineer-a", "run-6", "spec-6", "cancelled-package");
        using var cancellation = new CancellationTokenSource();

        var build = service.BuildAsync("engineer-a", workspace.WorkspaceId, cancellation.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
        var failed = await service.GetAsync("engineer-a", workspace.WorkspaceId);
        Assert.NotNull(failed);
        Assert.Equal(ConnectorWorkspaceStatuses.BuildFailed, failed!.Status);
        Assert.False(failed.LastBuild!.Succeeded);
    }

    [Fact]
    public void ContainerRunner_UsesIsolatedNamedVolumeSubpath()
    {
        var workspaceId = "019abcdef0123456789abcdef0123456";
        var workspaceRoot = Path.Combine(_root, "workspaces");
        var workspacePath = Path.Combine(workspaceRoot, workspaceId);
        Directory.CreateDirectory(workspacePath);
        var runner = new ConnectorCommandRunner(Options.Create(new ConnectorBuilderOptions
        {
            WorkspaceRoot = workspaceRoot,
            ContainerCommand = "docker",
            ContainerWorkspaceVolume = "ingot-connector-workspaces",
            DotnetSdkImage = "mcr.microsoft.com/dotnet/sdk:10.0"
        }));

        var containerName = runner.CreateContainerName(workspacePath, "build");
        var command = runner.BuildCommand(workspacePath, "build", containerName);
        var arguments = command.ArgumentList.ToArray();

        Assert.Equal("docker", command.FileName);
        Assert.Contains("none", arguments);
        Assert.Contains("never", arguments);
        Assert.Contains(containerName, arguments);
        Assert.Matches("^ingot-builder-[0-9a-f]{32}-build-[0-9a-f]{32}$", containerName);
        Assert.Contains("no-new-privileges", arguments);
        Assert.Contains("dotnet", arguments);
        Assert.Contains("build", arguments);
        Assert.Contains(
            $"type=volume,src=ingot-connector-workspaces,dst=/workspace,volume-subpath={workspaceId},volume-nocopy,readonly",
            arguments);
        Assert.Contains("/build:rw,nosuid,nodev,size=512m", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains(workspacePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContainerRunner_BuildsAndTestsGeneratedTemplate_WhenEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("INGOT_RUN_CONNECTOR_CONTAINER_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var options = Options.Create(new ConnectorBuilderOptions
        {
            WorkspaceRoot = Path.Combine(_root, "container-workspaces"),
            ArtifactRoot = Path.Combine(_root, "container-packages"),
            DotnetSdkImage = Environment.GetEnvironmentVariable("INGOT_CONNECTOR_BUILDER_IMAGE")
                              ?? "mcr.microsoft.com/dotnet/sdk:10.0",
            CommandTimeoutSeconds = 180
        });
        var service = new FileConnectorWorkspaceService(new ConnectorCommandRunner(options), options);
        var workspace = await service.CreateAsync(
            "engineer-a", "run-container", "spec-container", "container-smoke");

        var build = await service.BuildAsync("engineer-a", workspace.WorkspaceId);
        Assert.True(build.Result.Succeeded, build.Result.Output);
        var test = await service.TestAsync("engineer-a", workspace.WorkspaceId);
        Assert.True(test.Result.Succeeded, test.Result.Output);
        Assert.Equal(ConnectorWorkspaceStatuses.AwaitingPackageApproval, test.Workspace.Status);
    }

    [Fact]
    public void ContainerRunner_RejectsWorkspaceOutsideConfiguredRoot()
    {
        var workspaceRoot = Path.Combine(_root, "workspaces");
        var outside = Path.Combine(_root, "019abcdef0123456789abcdef0123456");
        var runner = new ConnectorCommandRunner(Options.Create(new ConnectorBuilderOptions
        {
            WorkspaceRoot = workspaceRoot,
            ContainerWorkspaceVolume = "ingot-connector-workspaces"
        }));

        Assert.Throws<UnauthorizedAccessException>(() => runner.CreateContainerName(outside, "test"));
    }

    [Fact]
    public void ContainerRunner_RejectsInvalidVolumeName()
    {
        var workspaceId = "019abcdef0123456789abcdef0123456";
        var workspaceRoot = Path.Combine(_root, "workspaces");
        var workspacePath = Path.Combine(workspaceRoot, workspaceId);
        Directory.CreateDirectory(workspacePath);
        var runner = new ConnectorCommandRunner(Options.Create(new ConnectorBuilderOptions
        {
            WorkspaceRoot = workspaceRoot,
            ContainerWorkspaceVolume = "invalid,readonly"
        }));
        var containerName = runner.CreateContainerName(workspacePath, "build");

        Assert.Throws<InvalidOperationException>(() =>
            runner.BuildCommand(workspacePath, "build", containerName));
    }

    [Fact]
    public void ContainerRunner_UsesSingleWorkspaceBindMountForLocalHost()
    {
        var workspaceId = "019abcdef0123456789abcdef0123456";
        var workspaceRoot = Path.Combine(_root, "workspaces");
        var workspacePath = Path.Combine(workspaceRoot, workspaceId);
        Directory.CreateDirectory(workspacePath);
        var runner = new ConnectorCommandRunner(Options.Create(new ConnectorBuilderOptions
        {
            WorkspaceRoot = workspaceRoot,
            ContainerWorkspaceVolume = string.Empty
        }));

        var containerName = runner.CreateContainerName(workspacePath, "test");
        var command = runner.BuildCommand(workspacePath, "test", containerName);

        Assert.Contains(
            $"type=bind,src={Path.GetFullPath(workspacePath)},dst=/workspace,readonly",
            command.ArgumentList);
        Assert.DoesNotContain("dotnet run", command.ArgumentList);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private FileConnectorWorkspaceService CreateService(IConnectorCommandRunner runner)
        => new(runner, Options.Create(new ConnectorBuilderOptions
        {
            WorkspaceRoot = Path.Combine(_root, "workspaces"),
            ArtifactRoot = Path.Combine(_root, "packages")
        }));

    private sealed class SuccessfulRunner : IConnectorCommandRunner
    {
        public Task<ConnectorCommandResult> RunAsync(
            string workspacePath,
            string operation,
            CancellationToken ct = default)
            => Task.FromResult(new ConnectorCommandResult
            {
                Operation = operation,
                Succeeded = true,
                ExitCode = 0,
                DurationMilliseconds = 1,
                Output = $"{operation} succeeded",
                CompletedAt = DateTimeOffset.UtcNow
            });
    }

    private sealed class BlockingRunner : IConnectorCommandRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ConnectorCommandResult> RunAsync(
            string workspacePath,
            string operation,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new ConnectorCommandResult
            {
                Operation = operation,
                Succeeded = true,
                ExitCode = 0,
                DurationMilliseconds = 1,
                Output = "completed",
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
