using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Ingot.Contracts.Acquisition;

public static class MqttTopicFilter
{

    public static bool IsValid(string? filter, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(filter))
        {
            error = "主题不能为空。";
            return false;
        }

        if (filter.Contains('\0'))
        {
            error = "主题不能包含空字符。";
            return false;
        }

        var levels = filter.Split('/');
        for (var index = 0; index < levels.Length; index++)
        {
            var level = levels[index];
            if (level.Contains('+') && level != "+")
            {
                error = "通配符 + 必须独占一个层级，例如 plant/+/line。";
                return false;
            }

            if (!level.Contains('#')) continue;
            if (level != "#")
            {
                error = "通配符 # 必须独占一个层级。";
                return false;
            }

            if (index != levels.Length - 1)
            {
                error = "通配符 # 只能出现在主题的最后一个层级。";
                return false;
            }
        }

        return true;
    }

    public static bool Matches(string? filter, string? topic)
    {
        if (filter is null || topic is null) return false;
        if (string.Equals(filter, topic, StringComparison.Ordinal)) return true;
        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');
        for (var index = 0; index < filterLevels.Length; index++)
        {
            var level = filterLevels[index];
            if (level == "#")
            {

                return index != 0 || !topicLevels[0].StartsWith('$');
            }

            if (index >= topicLevels.Length) return false;
            if (level == "+")
            {
                if (index == 0 && topicLevels[0].StartsWith('$')) return false;
                continue;
            }

            if (!string.Equals(level, topicLevels[index], StringComparison.Ordinal)) return false;
        }

        return filterLevels.Length == topicLevels.Length;
    }

    public static bool Intersects(string first, string second)
    {
        var left = first.Split('/');
        var right = second.Split('/');
        var index = 0;
        while (index < left.Length && index < right.Length)
        {
            if (left[index] == "#" || right[index] == "#") return true;
            if (left[index] != "+" && right[index] != "+" &&
                !string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
            index++;
        }
        if (index == left.Length && index == right.Length) return true;
        return index == left.Length
            ? index < right.Length && right[index] == "#"
            : index < left.Length && left[index] == "#";
    }
}

public static class JsonFieldSelector
{
    public const int MaximumSegments = 64;
    public const int MaximumArrayIndex = 10_000;

    public static bool IsValid(string? selector, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "JSON 字段路径不能为空。";
            return false;
        }
        var text = selector.Trim();
        if (text == ".") return true;
        return text.StartsWith("/", StringComparison.Ordinal)
            ? IsValidPointer(text, out error)
            : IsValidDottedPath(text, out error);
    }

    private static bool IsValidPointer(string text, out string error)
    {
        error = string.Empty;
        var segments = text.Split('/').Skip(1).ToArray();
        if (segments.Length > MaximumSegments)
        {
            error = $"JSON Pointer 最多包含 {MaximumSegments} 个层级。";
            return false;
        }
        foreach (var raw in segments)
        {
            for (var index = 0; index < raw.Length; index++)
            {
                if (raw[index] != '~') continue;
                if (++index >= raw.Length || raw[index] is not ('0' or '1'))
                {
                    error = "JSON Pointer 的 ~ 转义只能是 ~0 或 ~1。";
                    return false;
                }
            }
            if (raw.Length > 0 && raw.All(char.IsDigit) &&
                (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var arrayIndex) ||
                 arrayIndex > MaximumArrayIndex))
            {
                error = $"JSON 数组下标不能超过 {MaximumArrayIndex}。";
                return false;
            }
        }
        return true;
    }

    private static bool IsValidDottedPath(string text, out string error)
    {
        error = string.Empty;
        var segmentCount = 0;
        var rawSegments = text.Split('.', StringSplitOptions.None);
        if (rawSegments.Any(static segment => segment.Length == 0))
        {
            error = "JSON 字段路径不能以点开头或结尾，也不能包含连续的点。";
            return false;
        }
        foreach (var raw in rawSegments)
        {
            var cursor = 0;
            var bracket = raw.IndexOf('[');
            var nameLength = bracket < 0 ? raw.Length : bracket;
            if (nameLength > 0)
            {
                if (raw.AsSpan(0, nameLength).Contains(']'))
                {
                    error = "JSON 字段路径包含不匹配的数组括号。";
                    return false;
                }
                segmentCount++;
                cursor = nameLength;
            }
            while (cursor < raw.Length)
            {
                if (raw[cursor] != '[')
                {
                    error = "JSON 数组下标后不能附加未分隔文本。";
                    return false;
                }
                var close = raw.IndexOf(']', cursor + 1);
                if (close < 0 || close == cursor + 1 ||
                    !int.TryParse(raw.AsSpan(cursor + 1, close - cursor - 1),
                        NumberStyles.None, CultureInfo.InvariantCulture, out var arrayIndex) ||
                    arrayIndex > MaximumArrayIndex)
                {
                    error = $"JSON 数组下标必须是 0-{MaximumArrayIndex} 的整数。";
                    return false;
                }
                segmentCount++;
                cursor = close + 1;
            }
            if (nameLength == 0 && segmentCount == 0)
            {
                error = "JSON 字段路径缺少属性名或数组下标。";
                return false;
            }
        }
        if (segmentCount > MaximumSegments)
        {
            error = $"JSON 字段路径最多包含 {MaximumSegments} 个层级。";
            return false;
        }
        return true;
    }
}

public static class AcquisitionSelectors
{
    public const string BooleanDataType = "boolean";

    private static readonly string[] ModbusAreas =
        ["holding-register", "input-register", "coil", "discrete-input"];

    private static readonly string[] ByteOrders = ["big-endian", "little-endian"];
    private static readonly string[] WordOrders = ["high-low", "low-high"];

    public static bool IsModbusArea(string? value)
        => value is not null && Array.IndexOf(ModbusAreas, value) >= 0;

    public static bool IsModbusBitArea(string? value)
        => value is "coil" or "discrete-input";

    public static bool IsByteOrder(string? value)
        => value is not null && Array.IndexOf(ByteOrders, value) >= 0;

    public static bool IsWordOrder(string? value)
        => value is not null && Array.IndexOf(WordOrders, value) >= 0;

    public static IReadOnlyList<string> ModbusAreaValues => ModbusAreas;

    public sealed record ModbusPoint(
        string Area,
        ushort Address,
        string DataType,
        ushort Quantity,
        string ByteOrder,
        string WordOrder,
        int? BitIndex,
        ushort? ByteLength)
    {

        public ushort Span => Quantity;
    }

    public static bool TryParseModbus(
        string? selector,
        [NotNullWhen(true)] out ModbusPoint? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "Modbus 点位选择器不能为空。";
            return false;
        }

        var parts = selector.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            error = $"Modbus 点位选择器无效：{selector}。应使用 寄存器区:地址:类型 格式。";
            return false;
        }

        var area = parts[0];
        if (!IsModbusArea(area))
        {
            error = $"Modbus 寄存器区无效：{area}。";
            return false;
        }

        if (!TryParseAddressWithBit(parts[1], 10, out var address, out var bitIndex, out var addressError))
        {
            error = $"Modbus 地址无效：{addressError}";
            return false;
        }

        if (address > ushort.MaxValue)
        {
            error = $"Modbus 地址超出范围：{parts[1]}。";
            return false;
        }

        var dataType = parts[2];
        if (IsModbusBitArea(area))
        {
            if (dataType != BooleanDataType)
            {
                error = $"{area} 是位区，数据类型只能是 boolean。";
                return false;
            }

            if (bitIndex is not null)
            {
                error = "位区地址不需要再指定位偏移。";
                return false;
            }

            point = new ModbusPoint(area, (ushort)address, BooleanDataType, 1, "big-endian", "high-low", null, null);
            return true;
        }

        if (bitIndex is not null && dataType != BooleanDataType)
        {
            error = "指定位偏移时数据类型必须是 boolean。";
            return false;
        }

        if (dataType == BooleanDataType && bitIndex is null)
        {
            error = "保持/输入寄存器读取布尔值时必须指定位偏移，例如 holding-register:100.3:boolean。";
            return false;
        }

        ushort quantity;
        ushort? sourceByteLength = null;
        var byteOrder = "big-endian";
        var wordOrder = "high-low";
        if (dataType == "string")
        {
            if (parts.Length < 4 || !ushort.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var byteLength) ||
                byteLength is 0 or > 128)
            {
                error = "字符串点位必须在第 4 段指定 1-128 之间的字节长度，例如 holding-register:100:string:16。";
                return false;
            }

            quantity = (ushort)((byteLength + 1) / 2);
            sourceByteLength = byteLength;
        }
        else if (dataType == BooleanDataType)
        {
            quantity = 1;
        }
        else if (!AcquisitionProtocolCapabilities.IsRegisterDataType(dataType))
        {
            error = $"Modbus 数据类型无效：{dataType}。";
            return false;
        }
        else
        {
            quantity = (ushort)AcquisitionProtocolCapabilities.WordCountFor(dataType);
            if (parts.Length > 3 && parts[3].Length > 0)
            {
                if (!IsByteOrder(parts[3]))
                {
                    error = $"Modbus 字节序无效：{parts[3]}。";
                    return false;
                }

                byteOrder = parts[3];
            }

            if (parts.Length > 4 && parts[4].Length > 0)
            {
                if (!IsWordOrder(parts[4]))
                {
                    error = $"Modbus 字序无效：{parts[4]}。";
                    return false;
                }

                wordOrder = parts[4];
            }
        }

        if ((uint)address + quantity - 1 > ushort.MaxValue)
        {
            error = $"Modbus 点位 {selector} 的读取范围超出 0-65535 地址边界。";
            return false;
        }

        point = new ModbusPoint(area, (ushort)address, dataType, quantity, byteOrder, wordOrder, bitIndex, sourceByteLength);
        return true;
    }

    public static string FormatModbus(AcquisitionValueMapping mapping)
    {
        var address = mapping.BitIndex is { } bit && !IsModbusBitArea(mapping.ModbusArea)
            ? $"{mapping.ModbusAddress ?? 0}.{bit}"
            : (mapping.ModbusAddress ?? 0).ToString(CultureInfo.InvariantCulture);
        if (IsModbusBitArea(mapping.ModbusArea))
            return $"{mapping.ModbusArea}:{address}:{BooleanDataType}";
        if (mapping.SourceDataType == BooleanDataType)
            return $"{mapping.ModbusArea}:{address}:{BooleanDataType}";
        if (mapping.SourceDataType == "string")
            return $"{mapping.ModbusArea}:{address}:string:{mapping.SourceByteLength ?? Math.Max(1, mapping.ModbusQuantity * 2)}";
        return $"{mapping.ModbusArea}:{address}:{mapping.SourceDataType}:{mapping.ByteOrder}:{mapping.WordOrder}";
    }

    public sealed record MelsecDevice(string Code, bool IsBitDevice, int Radix, string Description);

    private static readonly Dictionary<string, MelsecDevice> MelsecDevices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["D"] = new MelsecDevice("D", false, 10, "数据寄存器"),
            ["R"] = new MelsecDevice("R", false, 10, "扩展文件寄存器"),
            ["W"] = new MelsecDevice("W", false, 16, "链接寄存器"),
            ["T"] = new MelsecDevice("T", false, 10, "定时器当前值"),
            ["C"] = new MelsecDevice("C", false, 10, "计数器当前值"),
            ["M"] = new MelsecDevice("M", true, 10, "辅助继电器"),
            ["L"] = new MelsecDevice("L", true, 10, "锁存继电器"),
            ["S"] = new MelsecDevice("S", true, 10, "状态继电器"),
            ["X"] = new MelsecDevice("X", true, 8, "输入继电器"),
            ["Y"] = new MelsecDevice("Y", true, 8, "输出继电器"),
            ["B"] = new MelsecDevice("B", true, 16, "链接继电器")
        };

    public static IReadOnlyCollection<MelsecDevice> MelsecDeviceCatalog => MelsecDevices.Values;

    public static bool TryGetMelsecDevice(string? code, [NotNullWhen(true)] out MelsecDevice? device)
    {
        if (code is not null && MelsecDevices.TryGetValue(code, out var found))
        {
            device = found;
            return true;
        }

        device = null;
        return false;
    }

    public sealed record MelsecPoint(
        MelsecDevice Device,
        uint WireAddress,
        string DisplayAddress,
        string DataType,
        int WordCount,
        int? BitIndex,
        ushort? ByteLength)
    {

        public bool UsesBitRead => DataType == BooleanDataType && Device.IsBitDevice && BitIndex is null;
    }

    public static bool TryParseMelsec(
        string? selector,
        [NotNullWhen(true)] out MelsecPoint? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "MELSEC 点位选择器不能为空。";
            return false;
        }

        var parts = selector.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            error = $"MELSEC 点位选择器无效：{selector}。应使用 软元件:编号:类型（如 D:100:int16）。";
            return false;
        }

        if (!TryGetMelsecDevice(parts[0], out var device))
        {
            error = $"MELSEC 软元件无效：{parts[0]}。";
            return false;
        }

        if (!TryParseAddressWithBit(parts[1], device.Radix, out var address, out var bitIndex, out var addressError))
        {
            error = $"MELSEC 软元件 {device.Code} 的编号无效：{addressError}";
            return false;
        }

        var dataType = parts[2];
        if (bitIndex is not null)
        {
            if (device.IsBitDevice)
            {
                error = $"{device.Code} 本身就是位软元件，编号中不需要位偏移。";
                return false;
            }

            if (dataType != BooleanDataType)
            {
                error = "指定位偏移时数据类型必须是 boolean。";
                return false;
            }
        }

        if (dataType == BooleanDataType)
        {
            if (!device.IsBitDevice && bitIndex is null)
            {
                error = $"{device.Code} 是字软元件，读取布尔值时必须指定位偏移，例如 {device.Code}:100.3:boolean。";
                return false;
            }

            point = new MelsecPoint(device, address, parts[1], BooleanDataType, 1, bitIndex, null);
            return true;
        }

        if (!AcquisitionProtocolCapabilities.IsRegisterDataType(dataType))
        {
            error = $"MELSEC 暂不支持的数据类型：{dataType}。";
            return false;
        }

        int wordCount;
        ushort? sourceByteLength = null;
        if (dataType == "string")
        {
            if (parts.Length < 4 ||
                !ushort.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var byteLength) ||
                byteLength is 0 or > 128)
            {
                error = "字符串点位必须在第 4 段指定 1-128 之间的字节长度，例如 D:100:string:16。";
                return false;
            }

            wordCount = (byteLength + 1) / 2;
            sourceByteLength = byteLength;
        }
        else
        {
            wordCount = AcquisitionProtocolCapabilities.WordCountFor(dataType);
        }

        if (address > uint.MaxValue - (uint)(wordCount - 1))
        {
            error = $"MELSEC 点位 {selector} 的读取范围超出软元件地址边界。";
            return false;
        }

        if (device.IsBitDevice && dataType != BooleanDataType)
        {

        }

        point = new MelsecPoint(device, address, parts[1], dataType, wordCount, null, sourceByteLength);
        return true;
    }

    public static string FormatMelsec(AcquisitionValueMapping mapping)
    {
        var address = mapping.BitIndex is { } bit
            ? $"{mapping.MelsecAddress}.{bit}"
            : mapping.MelsecAddress ?? string.Empty;
        var device = mapping.MelsecDevice ?? "D";
        return mapping.SourceDataType == "string"
            ? $"{device}:{address}:string:{mapping.SourceByteLength ?? Math.Max(1, mapping.ModbusQuantity * 2)}"
            : $"{device}:{address}:{mapping.SourceDataType}";
    }

    private static bool TryParseAddressWithBit(
        string value,
        int radix,
        out uint address,
        out int? bitIndex,
        [NotNullWhen(false)] out string? error)
    {
        address = 0;
        bitIndex = null;
        error = null;
        var separator = value.IndexOf('.');
        var addressText = separator < 0 ? value : value[..separator];
        if (separator >= 0)
        {
            var bitText = value[(separator + 1)..];
            if (!int.TryParse(bitText, NumberStyles.None, CultureInfo.InvariantCulture, out var bit) ||
                bit is < 0 or > 15)
            {
                error = $"位偏移必须是 0-15 之间的整数，实际为 {bitText}。";
                return false;
            }

            bitIndex = bit;
        }

        if (addressText.Length == 0)
        {
            error = "缺少地址。";
            return false;
        }

        return radix switch
        {
            8 => TryParseRadix(addressText, 8, "八进制编号只能包含数字 0-7", out address, out error),
            16 => TryParseRadix(addressText, 16, "十六进制编号只能包含 0-9 与 A-F", out address, out error),
            _ => TryParseRadix(addressText, 10, "编号只能包含数字 0-9", out address, out error)
        };
    }

    private static bool TryParseRadix(
        string text,
        int radix,
        string message,
        out uint value,
        [NotNullWhen(false)] out string? error)
    {
        value = 0;
        error = null;
        ulong accumulated = 0;
        foreach (var character in text)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => -1
            };
            if (digit < 0 || digit >= radix)
            {
                error = $"{message}，实际为 {text}。";
                return false;
            }

            accumulated = accumulated * (ulong)radix + (ulong)digit;
            if (accumulated > uint.MaxValue)
            {
                error = $"编号超出范围：{text}。";
                return false;
            }
        }

        value = (uint)accumulated;
        return true;
    }
}
