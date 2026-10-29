// Read declared Siemens S7 addresses through pinned, read-only ISO-on-TCP connections.
using System.Buffers.Binary;
using System.Text;
using Ingot.Contracts.Acquisition;
using S7.Net;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class SiemensS7ProtocolSupport
{
    private const int MaximumBytesPerRead = 200;

    public static async Task<Plc> OpenAsync(
        SiemensS7Connection connection,
        AcquisitionHttpEgressPolicy egressPolicy,
        int timeoutMs,
        CancellationToken ct)
    {
        var pinnedHost = await egressPolicy.ResolvePinnedHostAsync(
            connection.Host,
            "Siemens S7",
            ct).ConfigureAwait(false);
        if (!Enum.TryParse<CpuType>(connection.CpuType, ignoreCase: false, out var cpuType))
            throw new InvalidOperationException($"不支持 Siemens S7 CPU 类型：{connection.CpuType}。");

        var timeout = Math.Max(1000, timeoutMs);
        var plc = new Plc(cpuType, pinnedHost, connection.Port, (short)connection.Rack, (short)connection.Slot)
        {
            ReadTimeout = timeout,
            WriteTimeout = timeout
        };
        var openTask = plc.OpenAsync(ct);
        try
        {
            await openTask.WaitAsync(TimeSpan.FromMilliseconds(timeout), ct).ConfigureAwait(false);
            return plc;
        }
        catch (TimeoutException)
        {
            ((IDisposable)plc).Dispose();
            Observe(openTask);
            throw new TimeoutException($"Siemens S7 连接 {connection.Host}:{connection.Port} 超过 {timeout}ms 未完成。");
        }
        catch
        {
            ((IDisposable)plc).Dispose();
            Observe(openTask);
            throw;
        }
    }

    public static IReadOnlyDictionary<string, AcquisitionSelectors.SiemensS7AddressPoint> BuildSelectors(
        AcquisitionDeployment deployment)
    {
        var result = new Dictionary<string, AcquisitionSelectors.SiemensS7AddressPoint>(StringComparer.Ordinal);

        void Add(string? sourcePath, string? dataType = null, ushort? sourceByteLength = null, bool allowTimestampTypeAlias = false)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            if (!AcquisitionSelectors.TryParseSiemensS7Address(
                    sourcePath, dataType, sourceByteLength, out var point, out var error))
                throw new InvalidOperationException(error);

            var normalizedPath = sourcePath.Trim().ToUpperInvariant();
            if (result.TryGetValue(normalizedPath, out var existing))
            {
                if (allowTimestampTypeAlias && existing.ByteCount == point.ByteCount &&
                    IsIntegerTimestampType(existing.DataType) && IsIntegerTimestampType(point.DataType))
                    return;
                if (existing.DataType != point.DataType || existing.ByteCount != point.ByteCount)
                    throw new InvalidOperationException($"S7 地址 {normalizedPath} 在同一任务中被配置为不兼容的数据类型。");
                return;
            }
            result.Add(normalizedPath, point);
        }

        static bool IsIntegerTimestampType(string dataType)
            => dataType is "int16" or "uint16" or "int32" or "uint32" or "int64" or "uint64";

        var task = deployment.Task;
        foreach (var mapping in task.ValueMappings)
        {
            Add(mapping.SourcePath, mapping.SourceDataType, mapping.SourceByteLength);
            Add(mapping.QualityPath);
        }
        foreach (var mapping in task.ContextMappings)
            Add(mapping.SourcePath);
        if (task.TimestampMode == "source")
            Add(task.TimestampPath, allowTimestampTypeAlias: true);
        if (task.ProcessSpecification is { } processSpecification)
        {
            Add(processSpecification.IdPath);
            Add(processSpecification.VersionPath);
            Add(processSpecification.NamePath);
            foreach (var mapping in processSpecification.ParameterMappings)
            {
                Add(mapping.SourcePath, mapping.SourceDataType, mapping.SourceByteLength);
                Add(mapping.QualityPath);
            }
        }

        return result;
    }

    public static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        Plc plc,
        IReadOnlyDictionary<string, AcquisitionSelectors.SiemensS7AddressPoint> selectors,
        int maxMergeGap,
        int timeoutMs,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Math.Max(1000, timeoutMs));
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            foreach (var read in BuildReadPlan(selectors, maxMergeGap))
            {
                var bytes = await plc.ReadBytesAsync(
                    ToDataType(read.Area),
                    read.DataBlockNumber,
                    read.StartOffset,
                    read.Length,
                    timeout.Token).ConfigureAwait(false);
                foreach (var (path, point) in read.Points)
                {
                    var offset = point.ByteOffset - read.StartOffset;
                    values[path] = Decode(bytes.AsSpan(offset, point.ByteCount), point);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Siemens S7 完整读取超过 {Math.Max(1000, timeoutMs)}ms。");
        }

        return values;
    }

    private static IReadOnlyList<S7Read> BuildReadPlan(
        IReadOnlyDictionary<string, AcquisitionSelectors.SiemensS7AddressPoint> selectors,
        int maxMergeGap)
    {
        var reads = new List<S7Read>();
        foreach (var area in selectors
                     .OrderBy(static item => item.Value.Area, StringComparer.Ordinal)
                     .ThenBy(static item => item.Value.DataBlockNumber)
                     .ThenBy(static item => item.Value.ByteOffset)
                     .GroupBy(static item => (item.Value.Area, item.Value.DataBlockNumber)))
        {
            var pending = new List<KeyValuePair<string, AcquisitionSelectors.SiemensS7AddressPoint>>();
            var start = 0;
            var end = 0;
            foreach (var selector in area)
            {
                var point = selector.Value;
                var pointEnd = checked(point.ByteOffset + point.ByteCount);
                var gap = pending.Count == 0 ? 0 : Math.Max(0, point.ByteOffset - end);
                var wouldExceedReadLimit = pending.Count > 0 && pointEnd - start > MaximumBytesPerRead;
                if (pending.Count > 0 && (gap > maxMergeGap || wouldExceedReadLimit))
                {
                    reads.Add(new S7Read(area.Key.Area, area.Key.DataBlockNumber, start, end - start, pending));
                    pending = [];
                }

                if (pending.Count == 0)
                {
                    start = point.ByteOffset;
                    end = pointEnd;
                }
                else
                {
                    end = Math.Max(end, pointEnd);
                }
                pending.Add(selector);
            }

            if (pending.Count > 0)
                reads.Add(new S7Read(area.Key.Area, area.Key.DataBlockNumber, start, end - start, pending));
        }

        return reads;
    }

    private static DataType ToDataType(string area)
        => area switch
        {
            "DB" => DataType.DataBlock,
            "I" => DataType.Input,
            "Q" => DataType.Output,
            "M" => DataType.Memory,
            _ => throw new InvalidOperationException($"不支持 Siemens S7 存储区：{area}。")
        };

    private static object Decode(ReadOnlySpan<byte> bytes, AcquisitionSelectors.SiemensS7AddressPoint point)
    {
        if (point.BitIndex is { } bit)
            return (bytes[0] & (1 << bit)) != 0;

        return point.DataType switch
        {
            "byte" => bytes[0],
            "int16" => BinaryPrimitives.ReadInt16BigEndian(bytes),
            "uint16" => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            "int32" => BinaryPrimitives.ReadInt32BigEndian(bytes),
            "uint32" => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            "float32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes)),
            "int64" => BinaryPrimitives.ReadInt64BigEndian(bytes),
            "uint64" => BinaryPrimitives.ReadUInt64BigEndian(bytes),
            "float64" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes)),
            "string" => DecodeString(bytes, point.ByteCount - 2),
            _ => throw new InvalidOperationException($"不支持 Siemens S7 源数据类型：{point.DataType}。")
        };
    }

    private static string DecodeString(ReadOnlySpan<byte> bytes, int maximumLength)
    {
        if (bytes.Length < 2 || bytes[0] != maximumLength || bytes[1] > maximumLength || bytes[1] > bytes.Length - 2)
            throw new System.IO.InvalidDataException("S7 STRING 头部长度与配置不一致。");
        return Encoding.Latin1.GetString(bytes.Slice(2, bytes[1]));
    }

    private static void Observe(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = ObserveAsync(task);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // A timed-out open may finish after its socket has already been disposed.
        }
    }

    private sealed record S7Read(
        string Area,
        int DataBlockNumber,
        int StartOffset,
        int Length,
        IReadOnlyList<KeyValuePair<string, AcquisitionSelectors.SiemensS7AddressPoint>> Points);
}
