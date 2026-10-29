// Reads only declared EtherNet/IP tags through pinned, timeout-bounded libplctag handles.
using System.Net;
using Ingot.Contracts.Acquisition;
using libplctag;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class EtherNetIpProtocolSupport
{
    private const int DefaultPort = 44818;
    private static readonly HashSet<string> SupportedDataTypes = new(StringComparer.Ordinal)
    {
        "boolean", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64"
    };

    public static IReadOnlyList<EtherNetIpTagSelector> BuildSelectors(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        if (task.ContextMappings.Count > 0)
            throw new InvalidOperationException("EtherNet/IP 当前不支持未声明源数据类型的动态上下文标签。");
        if (task.ValueMappings.Any(static mapping => !string.IsNullOrWhiteSpace(mapping.QualityPath)))
            throw new InvalidOperationException("EtherNet/IP 当前不支持未声明类型的质量标签映射。");
        if (task.ProcessSpecification is not null)
            throw new InvalidOperationException("EtherNet/IP 当前不支持源数据类型未显式声明的配方版本字段。");

        var selectors = new Dictionary<string, EtherNetIpTagSelector>(StringComparer.Ordinal);

        void Add(string? sourcePath, string? dataType, bool allowNumericAlias = false)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;
            if (!AcquisitionSelectors.TryParseEtherNetIpTag(sourcePath, out var tagName, out var parseError))
                throw new InvalidOperationException(parseError);

            var normalizedType = dataType?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedType) || normalizedType == "auto" || !SupportedDataTypes.Contains(normalizedType))
                throw new InvalidOperationException(
                    $"EtherNet/IP 标签 {sourcePath} 必须声明受支持的标量数据类型；当前类型为 {dataType ?? "（未指定）"}。");

            if (selectors.TryGetValue(sourcePath, out var existing))
            {
                if (string.Equals(existing.DataType, normalizedType, StringComparison.Ordinal))
                    return;
                if (allowNumericAlias && IsInteger(existing.DataType) && IsInteger(normalizedType))
                    return;
                throw new InvalidOperationException(
                    $"EtherNet/IP 标签 {sourcePath} 在同一任务中被配置为不兼容的数据类型 {existing.DataType} 和 {normalizedType}。");
            }

            selectors.Add(sourcePath, new EtherNetIpTagSelector(sourcePath, tagName!, normalizedType));
        }

        foreach (var mapping in task.ValueMappings)
        {
            Add(mapping.SourcePath, mapping.SourceDataType);
            Add(mapping.QualityPath, "int32");
        }
        foreach (var mapping in task.ContextMappings)
            Add(mapping.SourcePath, "int32");

        if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
        {
            var timestampType = task.TimestampEncoding switch
            {
                AcquisitionTimestampEncodings.UnixSeconds => "uint32",
                AcquisitionTimestampEncodings.UnixMilliseconds => "int64",
                AcquisitionTimestampEncodings.Iso8601 => throw new InvalidOperationException(
                    "EtherNet/IP 当前仅支持数值型 Unix 秒或毫秒时间点；ISO-8601 字符串标签尚不支持。"),
                _ => "int32"
            };
            Add(task.TimestampPath, timestampType, allowNumericAlias: true);
        }

        Add(task.SequencePath, "int32");
        if (task.ProcessSpecification is { } processSpecification)
        {
            Add(processSpecification.IdPath, "int32");
            Add(processSpecification.VersionPath, "int32");
            if (!string.IsNullOrWhiteSpace(processSpecification.NamePath))
                throw new InvalidOperationException("EtherNet/IP 当前不支持字符串类型的工艺规格名称标签。");
            foreach (var mapping in processSpecification.ParameterMappings)
                Add(mapping.SourcePath, mapping.SourceDataType);
        }

        if (selectors.Count == 0)
            throw new InvalidOperationException("EtherNet/IP 不支持地址扫描；请先配置至少一个已知标签点位。");
        if (selectors.Count > 256)
            throw new InvalidOperationException($"EtherNet/IP 单任务最多读取 256 个不同标签，当前配置 {selectors.Count} 个。");
        return selectors.Values.ToArray();
    }

    public static async Task<EtherNetIpSession> OpenAsync(
        EtherNetIpConnection connection,
        IReadOnlyList<EtherNetIpTagSelector> selectors,
        AcquisitionHttpEgressPolicy egressPolicy,
        int timeoutMs,
        CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 500, 30_000));
        var pinnedHost = await egressPolicy.ResolvePinnedHostAsync(
            connection.Host,
            "EtherNet/IP",
            ct).ConfigureAwait(false);
        var gateway = FormatGateway(pinnedHost, connection.Port);
        var (plcType, path) = connection.ControllerFamily switch
        {
            "control-logix" => (PlcType.ControlLogix, connection.Path),
            "micro800" => (PlcType.Micro800, null),
            _ => throw new InvalidOperationException(
                $"不支持 EtherNet/IP 控制器系列 {connection.ControllerFamily}；支持 ControlLogix/CompactLogix 与 Micro800。")
        };

        var tags = new Dictionary<string, Tag>(StringComparer.Ordinal);
        using var initializationTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        initializationTimeout.CancelAfter(timeout);
        try
        {
            foreach (var selector in selectors)
            {
                var tag = new Tag
                {
                    Protocol = Protocol.ab_eip,
                    PlcType = plcType,
                    Gateway = gateway,
                    Name = selector.TagName,
                    Timeout = timeout,
                    ReadCacheMillisecondDuration = 0,
                    ElementCount = 1
                };
                if (path is not null)
                    tag.Path = path;

                tags.Add(selector.SourcePath, tag);
                await tag.InitializeAsync(initializationTimeout.Token).ConfigureAwait(false);
            }

            return new EtherNetIpSession(selectors, tags, timeout);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            DisposeTags(tags.Values);
            throw new TimeoutException($"EtherNet/IP 在 {timeout.TotalMilliseconds:0}ms 内未能初始化已配置标签。");
        }
        catch
        {
            DisposeTags(tags.Values);
            throw;
        }
    }

    private static string FormatGateway(string pinnedHost, int port)
    {
        if (!IPAddress.TryParse(pinnedHost, out var address))
            throw new InvalidOperationException("EtherNet/IP 出站策略未返回固定 IP 地址，已拒绝继续连接。");
        if (port == DefaultPort)
            return address.ToString();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
    }

    private static bool IsInteger(string dataType)
        => dataType is "int8" or "uint8" or "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64";

    private static void DisposeTags(IEnumerable<Tag> tags)
    {
        foreach (var tag in tags)
            tag.Dispose();
    }
}

internal sealed record EtherNetIpTagSelector(string SourcePath, string TagName, string DataType);

internal sealed class EtherNetIpSession : IDisposable
{
    private readonly IReadOnlyList<EtherNetIpTagSelector> _selectors;
    private readonly IReadOnlyDictionary<string, Tag> _tags;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public EtherNetIpSession(
        IReadOnlyList<EtherNetIpTagSelector> selectors,
        IReadOnlyDictionary<string, Tag> tags,
        TimeSpan timeout)
    {
        _selectors = selectors;
        _tags = tags;
        _timeout = timeout;
    }

    public async Task<Dictionary<string, object?>> ReadSnapshotAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readTimeout.CancelAfter(_timeout);
        var values = new Dictionary<string, object?>(_selectors.Count, StringComparer.Ordinal);
        try
        {
            foreach (var selector in _selectors)
            {
                var tag = _tags[selector.SourcePath];
                await tag.ReadAsync(readTimeout.Token).ConfigureAwait(false);
                values.Add(selector.SourcePath, ReadValue(tag, selector.DataType));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"EtherNet/IP 已配置标签读取超过 {_timeout.TotalMilliseconds:0}ms。");
        }

        return values;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var tag in _tags.Values)
            tag.Dispose();
    }

    private static object ReadValue(Tag tag, string dataType) => dataType switch
    {
        "boolean" => (object)tag.GetBit(0),
        "int8" => (object)tag.GetInt8(0),
        "uint8" => (object)tag.GetUInt8(0),
        "int16" => (object)tag.GetInt16(0),
        "uint16" => (object)tag.GetUInt16(0),
        "int32" => (object)tag.GetInt32(0),
        "uint32" => (object)tag.GetUInt32(0),
        "int64" => (object)tag.GetInt64(0),
        "uint64" => (object)tag.GetUInt64(0),
        "float32" => (object)tag.GetFloat32(0),
        "float64" => (object)tag.GetFloat64(0),
        _ => throw new InvalidOperationException($"EtherNet/IP 不支持源数据类型 {dataType}。")
    };
}
