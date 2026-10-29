// 限制串口采集只能打开 Edge 运维人员显式允许的物理端口。
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionSerialPortPolicy
{
    private static readonly Regex WindowsPortPattern = new("^COM[1-9][0-9]{0,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly HashSet<string> _allowedPorts;
    private readonly StringComparer _comparer;

    public AcquisitionSerialPortPolicy(IOptions<AcquisitionSecurityOptions> options)
    {
        _comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _allowedPorts = new HashSet<string>(_comparer);
        foreach (var configured in options.Value.AllowedSerialPorts ?? [])
        {
            if (!TryNormalizePortName(configured, out var normalized, out var error))
                throw new InvalidOperationException($"Acquisition:Security:AllowedSerialPorts 中包含无效端口：{error}");
            _allowedPorts.Add(normalized);
        }
    }

    public string RequireAllowedPort(string? portName)
    {
        if (!TryNormalizePortName(portName, out var normalized, out var error))
            throw new InvalidOperationException(error);
        var physicalPort = NormalizePhysicalPortName(normalized);
        var allowed = _allowedPorts.Contains(normalized) || _allowedPorts.Any(configured =>
            _comparer.Equals(NormalizePhysicalPortName(configured), physicalPort));
        if (!allowed)
        {
            throw new InvalidOperationException(
                $"串口 {physicalPort} 未加入 Edge 的 Acquisition:Security:AllowedSerialPorts 显式允许清单。");
        }
        return physicalPort;
    }

    public static bool TryNormalizePortName(string? portName, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var value = portName?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Any(char.IsControl))
        {
            error = "串口名不能为空或包含控制字符。";
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            if (value.StartsWith("\\\\.\\", StringComparison.Ordinal))
                value = value[4..];
            if (!WindowsPortPattern.IsMatch(value))
            {
                error = "Windows 串口名必须是 COM1-COM9999 或对应的 \\\\.\\COMn 形式。";
                return false;
            }
            normalized = value.ToUpperInvariant();
            return true;
        }

        var pathSegments = value.Split('/');
        if (!Path.IsPathFullyQualified(value) || pathSegments.Length < 3 || pathSegments[0].Length != 0 ||
            pathSegments[1] != "dev" ||
            pathSegments.Skip(2).Any(static segment => segment.Length == 0 || segment is "." or ".."))
        {
            error = "Unix 串口名必须是 /dev/ 下的绝对设备路径，且不能含有 . 或 .. 路径段。";
            return false;
        }

        normalized = Path.GetFullPath(value);
        return true;
    }

    private static string NormalizePhysicalPortName(string normalized)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(normalized))
            return normalized;

        try
        {
            var target = new FileInfo(normalized).ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? normalized : Path.GetFullPath(target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException(
                $"无法安全解析串口 {normalized} 的物理设备路径；为避免重复打开同一 RS-485 总线，Edge 已拒绝使用此别名。",
                exception);
        }
    }
}
