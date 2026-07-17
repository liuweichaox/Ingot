using System;
using System.Text;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     PLC 值访问辅助器。负责按数据类型读取寄存器并从批量缓冲区解码值。
/// </summary>
internal static class PlcValueAccessor
{
    public static async Task<object> ReadAsync(
        IPlcDataAccessClient client,
        string register,
        string dataType,
        int stringLength = 0,
        string? encoding = null)
    {
        return dataType.ToLowerInvariant() switch
        {
            "ushort" => await client.ReadUShortAsync(register).ConfigureAwait(false),
            "uint" => await client.ReadUIntAsync(register).ConfigureAwait(false),
            "ulong" => await client.ReadULongAsync(register).ConfigureAwait(false),
            "short" => await client.ReadShortAsync(register).ConfigureAwait(false),
            "int" => await client.ReadIntAsync(register).ConfigureAwait(false),
            "long" => await client.ReadLongAsync(register).ConfigureAwait(false),
            "float" => await client.ReadFloatAsync(register).ConfigureAwait(false),
            "double" => await client.ReadDoubleAsync(register).ConfigureAwait(false),
            "string" => SanitizeString(await client
                .ReadStringAsync(register, (ushort)stringLength, ResolveEncoding(encoding))
                .ConfigureAwait(false)),
            "bool" => await client.ReadBoolAsync(register).ConfigureAwait(false),
            _ => throw new NotSupportedException($"不支持的数据类型: {dataType}")
        };
    }

    public static object? Decode(
        IPlcDataAccessClient client,
        byte[] buffer,
        int index,
        int length,
        string dataType,
        string encoding)
    {
        return dataType.ToLowerInvariant() switch
        {
            "ushort" => client.TransUShort(buffer, index),
            "uint" => client.TransUInt(buffer, index),
            "ulong" => client.TransULong(buffer, index),
            "short" => client.TransShort(buffer, index),
            "int" => client.TransInt(buffer, index),
            "long" => client.TransLong(buffer, index),
            "float" => client.TransFloat(buffer, index),
            "double" => client.TransDouble(buffer, index),
            "string" => SanitizeString(client.TransString(buffer, index, length, ResolveEncoding(encoding))),
            "bool" => client.TransBool(buffer, index),
            _ => null
        };
    }

    public static bool IsTriggerActive(object? value)
    {
        if (value == null)
            return false;

        return value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s) && s != "0",
            _ => Convert.ToDecimal(value) != 0
        };
    }

    public static bool ShouldTrigger(AcquisitionTrigger? mode, object? previousValue, object? currentValue)
    {
        if (!mode.HasValue || previousValue == null || currentValue == null)
            return false;

        var previous = Convert.ToDecimal(previousValue);
        var current = Convert.ToDecimal(currentValue);

        return mode.Value switch
        {
            AcquisitionTrigger.RisingEdge => previous < current,
            AcquisitionTrigger.FallingEdge => previous > current,
            _ => false
        };
    }

    private static Encoding ResolveEncoding(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
            return Encoding.UTF8;

        var normalized = encoding.Trim().Replace("_", "-").ToLowerInvariant();
        return normalized switch
        {
            "utf8" or "utf-8" => Encoding.UTF8,
            "unicode" or "utf-16" => Encoding.Unicode,
            "utf-16be" => Encoding.BigEndianUnicode,
            "ascii" => Encoding.ASCII,
            _ => Encoding.GetEncoding(encoding)
        };
    }

    private static string SanitizeString(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.TrimEnd('\0');
}
