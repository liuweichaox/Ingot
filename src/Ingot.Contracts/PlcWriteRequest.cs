using System.Text.Json.Serialization;

namespace Ingot.Contracts;

/// <summary>
///     Plc 批量写入请求
/// </summary>
public class PlcWriteRequest
{
    private string _sourceCode = string.Empty;

    /// <summary>
    ///     数据源编码。v2 请求使用此字段。
    /// </summary>
    public string SourceCode
    {
        get => _sourceCode;
        set => _sourceCode = value?.Trim() ?? string.Empty;
    }

    /// <summary>
    ///     v1 请求兼容字段。读取请求时映射到 SourceCode，不再写入响应。
    /// </summary>
    [JsonPropertyName("PlcCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPlcCode
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(_sourceCode))
                _sourceCode = value?.Trim() ?? string.Empty;
        }
    }

    /// <summary>
    ///     写入项集合
    /// </summary>
    public List<PlcWriteItem> Items { get; set; } = new();
}

/// <summary>
///     Plc 写入项
/// </summary>
public class PlcWriteItem
{
    /// <summary>
    ///     寄存器地址
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    ///     数据类型
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    ///     写入值
    /// </summary>
    public object? Value { get; set; }
}
