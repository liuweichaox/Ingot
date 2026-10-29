// 统一解析跨协议点位地址，并集中定义各驱动支持的地址语法。
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

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
    private static readonly Regex SiemensDataBlockAddress = new(
        "^DB(?<db>[0-9]+)\\.DB(?<access>[XBWDL])(?<offset>[0-9]+)(?:\\.(?<bit>[0-7]))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SiemensAreaAddress = new(
        "^(?<area>I|Q|M)(?<access>[BWDL]?)(?<offset>[0-9]+)(?:\\.(?<bit>[0-7]))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EtherNetIpTagAddress = new(
        "^(?:Program:[A-Za-z_][A-Za-z0-9_]*\\.)?[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*|\\[[0-9]{1,10}\\])*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex OmronFinsAddress = new(
        "^(?<area>DM|CIO|WR|W|HR|H|AR|A):(?<address>[0-9]+)(?:\\.(?<bit>[0-9]{1,2}))?(?::(?<type>[a-z0-9]+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    public sealed record OmronFinsPoint(
        string Area,
        byte WordAreaCode,
        byte BitAreaCode,
        ushort Address,
        string DataType,
        int WordCount,
        int? BitIndex,
        string WordOrder);

    public static bool TryParseOmronFins(
        string? selector,
        string? dataType,
        string? wordOrder,
        [NotNullWhen(true)] out OmronFinsPoint? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "Omron FINS 点位地址不能为空。";
            return false;
        }

        var match = OmronFinsAddress.Match(selector.Trim());
        if (!match.Success)
        {
            error = $"Omron FINS 点位格式无效：{selector}。应使用 DM:100:int16 或 CIO:100.3:boolean。";
            return false;
        }

        var area = match.Groups["area"].Value.ToUpperInvariant() switch
        {
            "W" => "WR",
            "H" => "HR",
            "A" => "AR",
            var canonical => canonical
        };
        var (wordAreaCode, bitAreaCode) = area switch
        {
            "CIO" => ((byte)0xB0, (byte)0x30),
            "WR" => ((byte)0xB1, (byte)0x31),
            "HR" => ((byte)0xB2, (byte)0x32),
            "AR" => ((byte)0xB3, (byte)0x33),
            _ => ((byte)0x82, (byte)0x02)
        };
        if (!ushort.TryParse(match.Groups["address"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var address))
        {
            error = "Omron FINS 字地址必须在 0-65535 之间。";
            return false;
        }

        int? bitIndex = null;
        if (match.Groups["bit"].Success)
        {
            if (!int.TryParse(match.Groups["bit"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var bit) ||
                bit is < 0 or > 15)
            {
                error = "Omron FINS 位偏移必须在 0-15 之间。";
                return false;
            }
            bitIndex = bit;
        }

        var suffixType = match.Groups["type"].Success
            ? match.Groups["type"].Value.ToLowerInvariant()
            : null;
        var requestedType = string.IsNullOrWhiteSpace(dataType) || dataType == "auto"
            ? suffixType
            : dataType.Trim().ToLowerInvariant();
        if (requestedType is null)
        {
            error = "Omron FINS 点位必须显式声明源数据类型。";
            return false;
        }
        if (suffixType is not null && !string.Equals(suffixType, requestedType, StringComparison.Ordinal))
        {
            error = $"Omron FINS 地址中的数据类型 {suffixType} 与映射类型 {requestedType} 不一致。";
            return false;
        }
        if (!AcquisitionProtocolCapabilities.IsOmronFinsDataType(requestedType))
        {
            error = $"Omron FINS 暂不支持数据类型 {requestedType}。";
            return false;
        }
        if ((requestedType == BooleanDataType) != bitIndex.HasValue)
        {
            error = requestedType == BooleanDataType
                ? "Boolean 点位必须填写位偏移，例如 CIO:100.3:boolean。"
                : "只有 Boolean 点位可以填写位偏移。";
            return false;
        }
        var normalizedWordOrder = string.IsNullOrWhiteSpace(wordOrder)
            ? "high-low"
            : wordOrder.Trim().ToLowerInvariant();
        if (!IsWordOrder(normalizedWordOrder))
        {
            error = "Omron FINS 多字点位字序必须是 high-low 或 low-high。";
            return false;
        }

        var wordCount = requestedType == BooleanDataType ? 1 : AcquisitionProtocolCapabilities.WordCountFor(requestedType);
        if ((uint)address + (uint)wordCount > ushort.MaxValue + 1u)
        {
            error = "Omron FINS 点位的多字读取范围超出 0-65535 地址边界。";
            return false;
        }

        point = new OmronFinsPoint(area, wordAreaCode, bitAreaCode, address, requestedType, wordCount, bitIndex, normalizedWordOrder);
        return true;
    }

    public static string FormatOmronFins(OmronFinsPoint point)
    {
        var address = point.BitIndex is { } bit
            ? $"{point.Address.ToString(CultureInfo.InvariantCulture)}.{bit.ToString(CultureInfo.InvariantCulture)}"
            : point.Address.ToString(CultureInfo.InvariantCulture);
        return $"{point.Area}:{address}:{point.DataType}";
    }

    public static bool TryParseEtherNetIpTag(
        string? selector,
        [NotNullWhen(true)] out string? tag,
        [NotNullWhen(false)] out string? error)
    {
        tag = null;
        error = null;
        var candidate = selector?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            error = "EtherNet/IP 符号标签不能为空。";
            return false;
        }

        if (candidate.Length > 255 || !EtherNetIpTagAddress.IsMatch(candidate))
        {
            error = "EtherNet/IP 标签格式无效。支持 Tag、Tag.Member、Array[0] 和 Program:MainProgram.Tag；不支持字符串、UDT 路径语法或标签表达式。";
            return false;
        }

        foreach (Match index in Regex.Matches(candidate, "\\[([0-9]{1,10})\\]", RegexOptions.CultureInvariant))
        {
            if (!uint.TryParse(index.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                error = "EtherNet/IP 数组下标超出支持范围。";
                return false;
            }
        }

        tag = candidate;
        return true;
    }

    public sealed record SiemensS7AddressPoint(
        string Area,
        int DataBlockNumber,
        int ByteOffset,
        string AccessCode,
        string DataType,
        int ByteCount,
        int? BitIndex);

    public static bool TryParseSiemensS7Address(
        string? selector,
        string? dataType,
        ushort? sourceByteLength,
        [NotNullWhen(true)] out SiemensS7AddressPoint? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "S7 点位地址不能为空。";
            return false;
        }

        var address = selector.Trim().ToUpperInvariant();
        var dataBlockMatch = SiemensDataBlockAddress.Match(address);
        var areaMatch = dataBlockMatch.Success ? Match.Empty : SiemensAreaAddress.Match(address);
        if (!dataBlockMatch.Success && !areaMatch.Success)
        {
            error = $"S7 地址格式无效：{selector}。支持 DB1.DBW0、DB1.DBX0.0、MW10、I0.0 等绝对地址。";
            return false;
        }

        var isDataBlock = dataBlockMatch.Success;
        var match = isDataBlock ? dataBlockMatch : areaMatch;
        var area = isDataBlock ? "DB" : match.Groups["area"].Value;
        var access = match.Groups["access"].Value;
        var hasBit = match.Groups["bit"].Success;
        int? bitIndex = hasBit
            ? int.Parse(match.Groups["bit"].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;
        if (!int.TryParse(match.Groups["offset"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
            offset is < 0 or > 16_777_215)
        {
            error = "S7 字节偏移必须在 0-16777215 之间。";
            return false;
        }

        var dataBlockNumber = 0;
        if (isDataBlock &&
            (!int.TryParse(match.Groups["db"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out dataBlockNumber) ||
             dataBlockNumber is < 1 or > 65_535))
        {
            error = "DB 编号必须在 1-65535 之间。";
            return false;
        }

        if (hasBit && access is not ("X" or ""))
        {
            error = "S7 位偏移只能用于 DBX 或 M/I/Q 位地址，例如 DB1.DBX0.0 或 M0.0。";
            return false;
        }
        if (access == "X" && !hasBit)
        {
            error = "DBX 地址必须填写位偏移，例如 DB1.DBX0.0。";
            return false;
        }
        if (string.IsNullOrEmpty(access) && !hasBit)
        {
            error = "M/I/Q 地址必须指定字节、字、双字、长字或位偏移，例如 MW10 或 M0.0。";
            return false;
        }

        var requestedType = dataType?.Trim().ToLowerInvariant();
        var inferredType = hasBit ? BooleanDataType : access switch
        {
            "B" => "byte",
            "W" => "uint16",
            "D" => "uint32",
            "L" => "uint64",
            _ => string.Empty
        };
        var normalizedType = string.IsNullOrEmpty(requestedType) ? inferredType : requestedType;
        var expectedAccess = normalizedType switch
        {
            BooleanDataType => "X",
            "int16" or "uint16" => "W",
            "int32" or "uint32" or "float32" => "D",
            "int64" or "uint64" or "float64" => "L",
            "string" => "B",
            "byte" => "B",
            _ => string.Empty
        };
        if (expectedAccess.Length == 0)
        {
            error = $"S7 暂不支持源数据类型 {normalizedType}。";
            return false;
        }
        if (hasBit != (normalizedType == BooleanDataType) ||
            (hasBit && access is not ("X" or "")) ||
            (!hasBit && !string.Equals(access, expectedAccess, StringComparison.Ordinal)))
        {
            error = $"S7 地址类型 {address} 与源数据类型 {normalizedType} 不匹配。";
            return false;
        }
        if (normalizedType == "string" && (!isDataBlock || sourceByteLength is null or < 1 or > 198))
        {
            error = "S7 STRING 仅支持 DBB 地址，当前单次读取限制要求长度为 1-198 个字符。";
            return false;
        }
        if (normalizedType != "string" && sourceByteLength is not null)
        {
            error = "只有 S7 STRING 点位可以配置文本长度。";
            return false;
        }

        var byteCount = normalizedType switch
        {
            BooleanDataType or "byte" => 1,
            "int16" or "uint16" => 2,
            "int32" or "uint32" or "float32" => 4,
            "int64" or "uint64" or "float64" => 8,
            "string" => sourceByteLength!.Value + 2,
            _ => 0
        };
        if ((long)offset + byteCount > 16_777_216)
        {
            error = "S7 点位读取范围超出允许的字节地址边界。";
            return false;
        }

        point = new SiemensS7AddressPoint(area, dataBlockNumber, offset, access, normalizedType, byteCount, bitIndex);
        return true;
    }

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

    public sealed record MelsecMc3EDevice(
        string Code,
        ushort DeviceCode,
        bool IsBitDevice,
        int Radix,
        string Description);

    private const uint MelsecMc3EQnACompatibleMaximumAddress = 0x00FF_FFFF;

    private static readonly Dictionary<string, MelsecMc3EDevice> MelsecMc3EDevices =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["M"] = new MelsecMc3EDevice("M", 0x90, true, 10, "辅助继电器"),
            ["L"] = new MelsecMc3EDevice("L", 0x92, true, 10, "锁存继电器"),
            ["X"] = new MelsecMc3EDevice("X", 0x9C, true, 16, "输入继电器"),
            ["Y"] = new MelsecMc3EDevice("Y", 0x9D, true, 16, "输出继电器"),
            ["B"] = new MelsecMc3EDevice("B", 0xA0, true, 16, "链接继电器"),
            ["D"] = new MelsecMc3EDevice("D", 0xA8, false, 10, "数据寄存器"),
            ["W"] = new MelsecMc3EDevice("W", 0xB4, false, 16, "链接寄存器")
        };

    public static IReadOnlyCollection<MelsecMc3EDevice> MelsecMc3EDeviceCatalog
        => MelsecMc3EDevices.Values;

    public static bool TryGetMelsecMc3EDevice(
        string? code,
        [NotNullWhen(true)] out MelsecMc3EDevice? device)
    {
        if (code is not null && MelsecMc3EDevices.TryGetValue(code, out var found))
        {
            device = found;
            return true;
        }

        device = null;
        return false;
    }

    public sealed record MelsecMc3EPoint(
        MelsecMc3EDevice Device,
        uint Address,
        string DisplayAddress,
        string DataType,
        int WordCount,
        int? BitIndex,
        string? Family)
    {
        public bool UsesBitRead => DataType == BooleanDataType && Device.IsBitDevice && BitIndex is null;

        public int DeviceCodeLength => Family is null ? 0 : Family == MelsecMc3EFamilies.IqR ? 2 : 1;

        public int AddressLength => Family is null ? 0 : Family == MelsecMc3EFamilies.IqR ? 4 : 3;

        public uint MaximumAddress => Family is null or MelsecMc3EFamilies.IqR
            ? uint.MaxValue
            : MelsecMc3EQnACompatibleMaximumAddress;

        public bool HasWireLayout => Family is not null;
    }

    /// <summary>Parses the address grammar without selecting a family-specific wire layout.</summary>
    public static bool TryParseMelsecMc3E(
        string? selector,
        string? dataType,
        [NotNullWhen(true)] out MelsecMc3EPoint? point,
        [NotNullWhen(false)] out string? error)
        => TryParseMelsecMc3E(selector, dataType, family: null, out point, out error);

    /// <summary>Parses an MC 3E selector and applies the selected family's address width.</summary>
    public static bool TryParseMelsecMc3E(
        string? selector,
        string? dataType,
        string? family,
        [NotNullWhen(true)] out MelsecMc3EPoint? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        if (string.IsNullOrWhiteSpace(selector))
        {
            error = "MELSEC MC 3E 点位选择器不能为空。";
            return false;
        }

        var parts = selector.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3)
        {
            error = $"MELSEC MC 3E 点位选择器无效：{selector}。应使用 软元件:编号:类型（如 D:100:int16 或 X:10:boolean）。";
            return false;
        }

        var normalizedFamily = string.IsNullOrWhiteSpace(family) ? null : family.Trim().ToLowerInvariant();
        if (normalizedFamily is not null && !MelsecMc3EFamilies.IsSupported(normalizedFamily))
        {
            error = $"MELSEC MC 3E PLC 系列无效：{family}。支持 q-l、iq-r、iq-f。";
            return false;
        }

        var maximumAddress = normalizedFamily == MelsecMc3EFamilies.IqR
            ? uint.MaxValue
            : MelsecMc3EQnACompatibleMaximumAddress;
        if (normalizedFamily is null)
            maximumAddress = uint.MaxValue;

        if (!TryGetMelsecMc3EDevice(parts[0], out var device))
        {
            error = $"MELSEC MC 3E 软元件无效：{parts[0]}。首期支持 X、Y、M、L、B、D、W。";
            return false;
        }

        if (!TryParseMelsecMc3EAddress(
                parts[1], device.Radix, maximumAddress, out var address, out var bitIndex, out var addressError))
        {
            error = $"MELSEC MC 3E 软元件 {device.Code} 的编号无效：{addressError}";
            return false;
        }

        var selectorType = parts.Length == 3 ? parts[2].ToLowerInvariant() : null;
        var requestedType = string.IsNullOrWhiteSpace(dataType) ? null : dataType.Trim().ToLowerInvariant();
        if (selectorType is not null && requestedType is not null &&
            !string.Equals(selectorType, requestedType, StringComparison.Ordinal))
        {
            error = $"MELSEC MC 3E 选择器类型 {selectorType} 与配置的数据类型 {requestedType} 不一致。";
            return false;
        }

        var normalizedType = selectorType ?? requestedType;
        if (normalizedType is null)
        {
            error = "MELSEC MC 3E 点位必须在选择器中指定数据类型，或由点位映射显式提供数据类型。";
            return false;
        }

        if (!AcquisitionProtocolCapabilities.IsMelsecMc3EDataType(normalizedType))
        {
            error = $"MELSEC MC 3E 暂不支持的数据类型：{normalizedType}。";
            return false;
        }

        if (device.IsBitDevice)
        {
            if (normalizedType != BooleanDataType)
            {
                error = $"{device.Code} 是位软元件，只支持 boolean。";
                return false;
            }

            if (bitIndex is not null)
            {
                error = $"{device.Code} 本身就是位软元件，编号中不需要位偏移。";
                return false;
            }
        }
        else if (normalizedType == BooleanDataType && bitIndex is null)
        {
            error = $"{device.Code} 是字软元件，读取布尔值时必须指定 0-15 位偏移，例如 {device.Code}:100.3:boolean。";
            return false;
        }
        else if (normalizedType != BooleanDataType && bitIndex is not null)
        {
            error = "指定字内位偏移时数据类型必须是 boolean。";
            return false;
        }

        var wordCount = normalizedType == BooleanDataType
            ? 1
            : AcquisitionProtocolCapabilities.WordCountFor(normalizedType);
        if (address > maximumAddress - (uint)(wordCount - 1))
        {
            error = $"MELSEC MC 3E 点位 {selector} 的读取范围超出当前 PLC 系列地址边界 0-{maximumAddress:X}。";
            return false;
        }

        var displayAddress = FormatMelsecMc3EAddress(address, bitIndex, device.Radix);
        point = new MelsecMc3EPoint(device, address, displayAddress, normalizedType, wordCount, bitIndex, normalizedFamily);
        return true;
    }

    public static string FormatMelsecMc3E(MelsecMc3EPoint point)
        => $"{point.Device.Code}:{point.DisplayAddress}:{point.DataType}";

    private static bool TryParseMelsecMc3EAddress(
        string value,
        int radix,
        uint maximumAddress,
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
            if (value.IndexOf('.', separator + 1) >= 0)
            {
                error = "地址最多只能包含一个字内位分隔符。";
                return false;
            }

            var bitText = value[(separator + 1)..];
            if (!int.TryParse(bitText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBit) ||
                parsedBit is < 0 or > 15)
            {
                error = $"字内位偏移必须是 0-15 之间的整数，实际为 {bitText}。";
                return false;
            }

            bitIndex = parsedBit;
        }

        if (addressText.Length == 0)
        {
            error = "缺少地址。";
            return false;
        }

        if (!TryParseMelsecMc3ERadix(addressText, radix, maximumAddress, out address, out error))
            return false;
        if (address > maximumAddress)
        {
            error = $"当前 PLC 系列设备地址不能超过 0x{maximumAddress:X}。";
            return false;
        }

        return true;
    }

    private static bool TryParseMelsecMc3ERadix(
        string text,
        int radix,
        uint maximumAddress,
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
                error = radix == 16
                    ? $"十六进制编号只能包含 0-9 与 A-F，实际为 {text}。"
                    : $"十进制编号只能包含数字 0-9，实际为 {text}。";
                return false;
            }

            var digitValue = (uint)digit;
            if (accumulated > ((ulong)maximumAddress - digitValue) / (uint)radix)
            {
                error = $"当前 PLC 系列设备地址不能超过 0x{maximumAddress:X}。";
                return false;
            }

            accumulated = accumulated * (uint)radix + digitValue;
        }

        value = (uint)accumulated;
        return true;
    }

    private static string FormatMelsecMc3EAddress(uint address, int? bitIndex, int radix)
    {
        var formattedAddress = radix == 16
            ? address.ToString("X", CultureInfo.InvariantCulture)
            : address.ToString(CultureInfo.InvariantCulture);
        return bitIndex is { } bit ? $"{formattedAddress}.{bit}" : formattedAddress;
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

    public sealed record MtConnectDataItemSelector(string Id);

    public sealed record BacnetObjectSelector(string ObjectType, uint Instance);

    public sealed record SnmpOidSelector(string Value, IReadOnlyList<uint> Arcs);

    /// <summary>Parses a canonical numeric SNMP OID and rejects symbolic MIB names.</summary>
    public static bool TryParseSnmpOid(
        string? selector,
        [NotNullWhen(true)] out SnmpOidSelector? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        var text = selector?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "SNMP OID 不能为空；必须使用数字形式，例如 1.3.6.1.2.1.1.1.0。";
            return false;
        }

        if (text.StartsWith(".", StringComparison.Ordinal))
            text = text[1..];
        var segments = text.Split('.');
        if (segments.Length is < 2 or > 128)
        {
            error = "SNMP OID 必须包含 2-128 个数字 arc。";
            return false;
        }

        var arcs = new uint[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0 ||
                segment.Length > 1 && segment[0] == '0' ||
                segment.Any(static character => character is < '0' or > '9') ||
                !uint.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out arcs[index]))
            {
                error = "SNMP OID 只允许规范十进制数字 arc（不能有前导零、符号或 MIB 名称），每个 arc 最大为 uint32。";
                return false;
            }
        }

        if (arcs[0] > 2 || arcs[0] < 2 && arcs[1] > 39)
        {
            error = "SNMP OID 的首 arc 必须是 0、1 或 2；首 arc 为 0 或 1 时第二 arc 最大为 39。";
            return false;
        }

        point = new SnmpOidSelector(
            string.Join(".", arcs.Select(static arc => arc.ToString(CultureInfo.InvariantCulture))),
            arcs);
        return true;
    }

    public static string FormatSnmpOid(SnmpOidSelector point) => point.Value;

    private static readonly HashSet<string> BacnetPresentValueObjectTypes = new(StringComparer.Ordinal)
    {
        "analog-input", "analog-output", "analog-value",
        "binary-input", "binary-output", "binary-value",
        "multi-state-input", "multi-state-output", "multi-state-value"
    };

    /// <summary>Validates a scalar BACnet object with a standard Present_Value property.</summary>
    public static bool TryParseBacnetObject(
        string? selector,
        [NotNullWhen(true)] out BacnetObjectSelector? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        var text = selector?.Trim();
        var separator = text?.IndexOf(':') ?? -1;
        if (string.IsNullOrWhiteSpace(text) || separator <= 0 || separator == text.Length - 1 ||
            text.IndexOf(':', separator + 1) >= 0)
        {
            error = "BACnet 对象地址格式应为 analog-input:1 等 object-type:instance。";
            return false;
        }

        var objectType = text[..separator].ToLowerInvariant();
        if (!BacnetPresentValueObjectTypes.Contains(objectType))
        {
            error = "BACnet 首期仅支持 analog、binary 和 multi-state 的 input/output/value 对象。";
            return false;
        }

        var instanceText = text[(separator + 1)..];
        if (!uint.TryParse(instanceText, NumberStyles.None, CultureInfo.InvariantCulture, out var instance) ||
            instance > 4_194_302)
        {
            error = "BACnet 对象实例必须是 0-4194302 之间的十进制整数。";
            return false;
        }

        point = new BacnetObjectSelector(objectType, instance);
        return true;
    }

    public static string FormatBacnetObject(BacnetObjectSelector point)
        => $"{point.ObjectType}:{point.Instance.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Validates an exact, case-sensitive MTConnect DataItem XML ID.</summary>
    public static bool TryParseMtConnectDataItem(
        string? selector,
        [NotNullWhen(true)] out MtConnectDataItemSelector? point,
        [NotNullWhen(false)] out string? error)
    {
        point = null;
        error = null;
        if (string.IsNullOrEmpty(selector))
        {
            error = "MTConnect DataItem ID 不能为空。";
            return false;
        }

        if (selector.Length > 256)
        {
            error = "MTConnect DataItem ID 不能超过 256 个字符。";
            return false;
        }

        if (!string.Equals(selector, selector.Trim(), StringComparison.Ordinal))
        {
            error = "MTConnect DataItem ID 不能包含首尾空白。";
            return false;
        }

        try
        {
            XmlConvert.VerifyNCName(selector);
        }
        catch (XmlException)
        {
            error = "MTConnect DataItem ID 必须是有效的 XML ID。";
            return false;
        }
        catch (ArgumentException)
        {
            error = "MTConnect DataItem ID 必须是有效的 XML ID。";
            return false;
        }

        point = new MtConnectDataItemSelector(selector);
        return true;
    }

    public static string FormatMtConnectDataItem(MtConnectDataItemSelector point)
        => point.Id;
}
