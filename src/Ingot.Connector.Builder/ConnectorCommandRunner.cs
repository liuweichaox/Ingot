using System.Diagnostics;
using System.Text.RegularExpressions;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;

namespace Ingot.Connector.Builder;

public sealed partial class ConnectorCommandRunner(IOptions<ConnectorBuilderOptions> options) : IConnectorCommandRunner
{
    private readonly ConnectorBuilderOptions _options = options.Value;

    public async Task<ConnectorCommandResult> RunAsync(
        string workspacePath,
        string operation,
        CancellationToken ct = default)
    {
        var containerName = CreateContainerName(workspacePath, operation);
        var command = BuildCommand(workspacePath, operation, containerName);
        var started = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.CommandTimeoutSeconds, 5, 900)));

        using var process = new Process
        {
            StartInfo = command,
            EnableRaisingEvents = true
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The CLI exited between the state check and the kill request.
            }
            await RemoveContainerAsync(containerName).ConfigureAwait(false);
            throw;
        }

        var output = string.Concat(await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        var max = Math.Clamp(_options.MaxOutputCharacters, 1_000, 200_000);
        if (output.Length > max)
            output = output[^max..];
        return new ConnectorCommandResult
        {
            Operation = operation,
            Succeeded = process.ExitCode == 0,
            ExitCode = process.ExitCode,
            DurationMilliseconds = started.ElapsedMilliseconds,
            Output = output,
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    internal ProcessStartInfo BuildCommand(string workspacePath, string operation, string containerName)
    {
        var arguments = operation switch
        {
            "build" => new[]
            {
                "build", "tests/Connector.Tests.csproj", "--nologo", "--artifacts-path", "/build/artifacts"
            },
            "test" => new[]
            {
                "run", "--project", "tests/Connector.Tests.csproj", "--artifacts-path", "/build/artifacts"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "只允许 build 或 test。")
        };

        var workspaceSubpath = GetContainerWorkspaceSubpath(workspacePath);
        if (!ContainerNameRegex().IsMatch(containerName))
            throw new ArgumentException("连接器构建容器名称格式不合法。", nameof(containerName));
        var volume = _options.ContainerWorkspaceVolume.Trim();
        string workspaceMount;
        if (volume.Length > 0)
        {
            if (!ContainerVolumeRegex().IsMatch(volume))
                throw new InvalidOperationException("ConnectorBuilder:ContainerWorkspaceVolume 格式不合法。");
            workspaceMount =
                $"type=volume,src={volume},dst=/workspace,volume-subpath={workspaceSubpath},volume-nocopy,readonly";
        }
        else
        {
            var fullWorkspacePath = Path.GetFullPath(workspacePath);
            workspaceMount = $"type=bind,src={fullWorkspacePath},dst=/workspace,readonly";
        }

        var start = BaseStartInfo(_options.ContainerCommand, workspacePath);
        foreach (var argument in new[]
                 {
                     "run", "--rm", "--name", containerName, "--pull", "never", "--network", "none",
                     "--cpus", "1", "--memory", "768m",
                     "--pids-limit", "128", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                     "--read-only", "--tmpfs", "/tmp:rw,nosuid,nodev,noexec,size=256m",
                     "--tmpfs", "/build:rw,nosuid,nodev,size=512m",
                     "--ulimit", "nofile=1024:1024",
                     "-e", "HOME=/tmp", "-e", "DOTNET_CLI_HOME=/tmp/dotnet",
                     "-e", "NUGET_PACKAGES=/tmp/nuget", "-e", "DOTNET_CLI_TELEMETRY_OPTOUT=1",
                     "-e", "DOTNET_NOLOGO=1",
                     "--mount", workspaceMount,
                     "-w", "/workspace", "--entrypoint", "dotnet", _options.DotnetSdkImage
                 })
            start.ArgumentList.Add(argument);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    internal string CreateContainerName(string workspacePath, string operation)
    {
        if (operation is not ("build" or "test"))
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "只允许 build 或 test。");
        var workspaceSubpath = GetContainerWorkspaceSubpath(workspacePath);
        return $"ingot-builder-{workspaceSubpath}-{operation}-{Guid.NewGuid():N}";
    }

    private async Task RemoveContainerAsync(string containerName)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cleanup = new Process
        {
            StartInfo = BaseStartInfo(_options.ContainerCommand, Directory.GetCurrentDirectory()),
            EnableRaisingEvents = true
        };
        cleanup.StartInfo.ArgumentList.Add("rm");
        cleanup.StartInfo.ArgumentList.Add("-f");
        cleanup.StartInfo.ArgumentList.Add(containerName);
        try
        {
            cleanup.Start();
            var stdout = cleanup.StandardOutput.ReadToEndAsync(cleanupTimeout.Token);
            var stderr = cleanup.StandardError.ReadToEndAsync(cleanupTimeout.Token);
            await cleanup.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                if (!cleanup.HasExited)
                    cleanup.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Cleanup did not start or already exited.
            }
        }
    }

    private string GetContainerWorkspaceSubpath(string workspacePath)
    {
        var workspaceRoot = Path.GetFullPath(_options.WorkspaceRoot);
        var fullWorkspacePath = Path.GetFullPath(workspacePath);
        if (!fullWorkspacePath.StartsWith(workspaceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("连接器构建路径不属于配置的工作区根目录。");

        var relative = Path.GetRelativePath(workspaceRoot, fullWorkspacePath).Replace('\\', '/');
        if (!WorkspaceSubpathRegex().IsMatch(relative))
            throw new UnauthorizedAccessException("连接器构建路径必须指向单个受控工作区。");
        return relative;
    }

    private static ProcessStartInfo BaseStartInfo(string fileName, string workingDirectory)
        => new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerVolumeRegex();

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceSubpathRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContainerNameRegex();
}
