using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Ingot.Contracts.Acquisition;

/// <summary>
///     MQTT 主题过滤器的语法校验与匹配。
///     校验器用它拒绝非法过滤器，采集节点用它判断一条报文属于哪个订阅——
///     两边必须是同一份规则，否则界面上能配的绑定在现场不生效。
/// </summary>
public static class MqttTopicFilter
{
    /// <summary>+ 必须独占一层，# 只能出现在最后一层。</summary>
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

    /// <summary>判断一条具体主题的报文是否命中该过滤器。</summary>
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
                // # 匹配剩余所有层级，但按 MQTT 规范不匹配以 $ 开头的系统主题。
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
}

/// <summary>
///     寄存器类协议的点位选择器解析。
///
///     这些语法以前分别写在 <c>ModbusTcpAcquisitionRunner</c> 与
///     <c>MelsecA1EAcquisitionRunner</c> 内部，平台侧无法复用，导致选择器语法错误
///     要等到边缘节点真正连接设备时才暴露。解析移到公共契约后，平台保存配置时
///     就能给出精确到字段的错误，边缘节点与配置界面共用同一套规则。
/// </summary>
public static class AcquisitionSelectors
{
    public const string BooleanDataType = "boolean";

    private static readonly string[] ModbusAreas =
        ["holding-register", "input-register", "coil", "discrete-input"];

    private static readonly string[] ByteOrders = ["big-endian", "little-endian"];
    private static readonly string[] WordOrders = ["high-low", "low-high"];

    public static bool IsModbusArea(string? value)
        => value is not null && Array.IndexOf(ModbusAreas, value) >= 0;

    /// <summary>线圈与离散输入天然是位区，读取结果始终是布尔值。</summary>
    public static bool IsModbusBitArea(string? value)
        => value is "coil" or "discrete-input";

    public static bool IsByteOrder(string? value)
        => value is not null && Array.IndexOf(ByteOrders, value) >= 0;

    public static bool IsWordOrder(string? value)
        => value is not null && Array.IndexOf(WordOrders, value) >= 0;

    public static IReadOnlyList<string> ModbusAreaValues => ModbusAreas;

    // ---------------------------------------------------------------- Modbus

    /// <summary>
    ///     Modbus 点位。<see cref="BitIndex"/> 非空表示从保持/输入寄存器的某一位取布尔值。
    /// </summary>
    public sealed record ModbusPoint(
        string Area,
        ushort Address,
        string DataType,
        ushort Quantity,
        string ByteOrder,
        string WordOrder,
        int? BitIndex)
    {
        /// <summary>该点位覆盖的寄存器（或线圈）数量，用于分块合并读取。</summary>
        public ushort Span => Quantity;
    }

    /// <summary>
    ///     解析 <c>area:address:type[:extra][:wordOrder]</c>。
    ///     地址允许 <c>100.3</c> 形式表示位偏移，此时类型必须是 boolean。
    ///     string 类型的第 4 段是字节长度，其余类型的第 4 段是字节序、第 5 段是字序。
    /// </summary>
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

            point = new ModbusPoint(area, (ushort)address, BooleanDataType, 1, "big-endian", "high-low", null);
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

        point = new ModbusPoint(area, (ushort)address, dataType, quantity, byteOrder, wordOrder, bitIndex);
        return true;
    }

    /// <summary>把结构化的值映射还原为规范选择器字符串，保证与解析端严格互逆。</summary>
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
            return $"{mapping.ModbusArea}:{address}:string:{Math.Max(1, mapping.ModbusQuantity * 2)}";
        return $"{mapping.ModbusArea}:{address}:{mapping.SourceDataType}:{mapping.ByteOrder}:{mapping.WordOrder}";
    }

    // ---------------------------------------------------------------- MELSEC

    /// <summary>
    ///     MELSEC 软元件定义。<see cref="IsBitDevice"/> 决定读取时使用位单位批量读还是字单位批量读；
    ///     <see cref="Radix"/> 是该软元件在设备手册中的编号进制。
    ///
    ///     X / Y 在 FX 系列按八进制编号，B / W 按十六进制编号。界面会同时显示换算后的
    ///     软元件编号，便于工程师对照手册确认第一次接线。
    /// </summary>
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

    /// <param name="WireAddress">按软元件进制换算后、真正写进 1E 帧的软元件编号。</param>
    public sealed record MelsecPoint(
        MelsecDevice Device,
        uint WireAddress,
        string DisplayAddress,
        string DataType,
        int WordCount,
        int? BitIndex)
    {
        /// <summary>布尔值读取位软元件时使用位单位批量读命令（0x00），其余使用字单位批量读（0x01）。</summary>
        public bool UsesBitRead => DataType == BooleanDataType && Device.IsBitDevice && BitIndex is null;
    }

    /// <summary>
    ///     解析 <c>软元件:编号:类型[:字节长度]</c>。
    ///     编号允许 <c>100.3</c> 形式表示字软元件内的位偏移，此时类型必须是 boolean。
    /// </summary>
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

            point = new MelsecPoint(device, address, parts[1], BooleanDataType, 1, bitIndex);
            return true;
        }

        if (!AcquisitionProtocolCapabilities.IsRegisterDataType(dataType))
        {
            error = $"MELSEC 暂不支持的数据类型：{dataType}。";
            return false;
        }

        int wordCount;
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
        }
        else
        {
            wordCount = AcquisitionProtocolCapabilities.WordCountFor(dataType);
        }

        if (device.IsBitDevice && dataType != BooleanDataType)
        {
            // 合法但危险：按字读取位软元件会把 16 个连续点打包成一个字。
            // 允许通过，界面负责显示提醒，避免工程师误以为读到的是单点状态。
        }

        point = new MelsecPoint(device, address, parts[1], dataType, wordCount, null);
        return true;
    }

    public static string FormatMelsec(AcquisitionValueMapping mapping)
    {
        var address = mapping.BitIndex is { } bit
            ? $"{mapping.MelsecAddress}.{bit}"
            : mapping.MelsecAddress ?? string.Empty;
        var device = mapping.MelsecDevice ?? "D";
        return mapping.SourceDataType == "string"
            ? $"{device}:{address}:string:{Math.Max(1, mapping.ModbusQuantity * 2)}"
            : $"{device}:{address}:{mapping.SourceDataType}";
    }

    // ---------------------------------------------------------------- 公共

    /// <summary>解析 <c>100</c> 或 <c>100.3</c>；地址部分按软元件进制解析，位偏移固定十进制 0-15。</summary>
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
