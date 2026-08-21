
using System.Globalization;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class AcquisitionTimestampParser
{
    public static DateTimeOffset Parse(
        object? raw,
        string encoding,
        string path,
        DateTimeOffset? receivedAt = null,
        int maximumFutureSkewMs = 300_000)
    {
        if (raw is null)
            throw new InvalidDataException($"配置的时间来源没有读到值：{path}。");
        try
        {
            var timestamp = encoding switch
            {
                AcquisitionTimestampEncodings.UnixSeconds => DateTimeOffset.FromUnixTimeSeconds(
                    Convert.ToInt64(raw, CultureInfo.InvariantCulture)),
                AcquisitionTimestampEncodings.UnixMilliseconds => DateTimeOffset.FromUnixTimeMilliseconds(
                    Convert.ToInt64(raw, CultureInfo.InvariantCulture)),
                AcquisitionTimestampEncodings.Iso8601 when DateTimeOffset.TryParse(
                    Convert.ToString(raw, CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedIsoTimestamp) => parsedIsoTimestamp,
                _ => throw new InvalidDataException($"不支持的时间戳编码：{encoding}。")
            };
            var observedAt = receivedAt ?? DateTimeOffset.UtcNow;
            if (maximumFutureSkewMs > 0 &&
                timestamp > observedAt.AddMilliseconds(maximumFutureSkewMs))
            {
                throw new InvalidDataException(
                    $"设备时间戳超前 Edge 接收时间超过 {maximumFutureSkewMs}ms：{path}={raw}。");
            }
            return timestamp;
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"设备时间戳格式无效：{path}={raw}，编码={encoding}。",
                exception);
        }
    }
}
