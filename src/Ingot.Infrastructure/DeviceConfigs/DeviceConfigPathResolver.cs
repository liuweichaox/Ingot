using System;
using System.IO;

namespace Ingot.Infrastructure.DeviceConfigs;

/// <summary>
///     统一解析设备配置目录，确保服务运行和离线校验使用相同的路径规则。
/// </summary>
public static class DeviceConfigPathResolver
{
    public static string Resolve(string? configDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(configDirectory) ? "Configs" : configDirectory.Trim();
        return Path.IsPathRooted(directory)
            ? directory
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, directory));
    }

    /// <summary>
    ///     解析命令行传入的目录。相对路径优先按当前目录解释；若不存在，
    ///     再按包含 Ingot.sln 的仓库根目录解释，兼容 dotnet run 的工作目录行为。
    /// </summary>
    public static string ResolveCommandLinePath(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("配置目录不能为空", nameof(directory));

        if (Path.IsPathRooted(directory))
            return Path.GetFullPath(directory);

        var currentDirectoryCandidate = Path.GetFullPath(directory);
        if (Directory.Exists(currentDirectoryCandidate))
            return currentDirectoryCandidate;

        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        return repositoryRoot is null
            ? currentDirectoryCandidate
            : Path.GetFullPath(Path.Combine(repositoryRoot, directory));
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ingot.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }
}
