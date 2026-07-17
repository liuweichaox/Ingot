namespace Ingot.Domain.Models;

/// <summary>
///     通道内单个指标配置。
/// </summary>
public class Metric
{
    /// <summary>
    ///     指标标签。
    /// </summary>
    public string MetricLabel { get; set; } = string.Empty;

    /// <summary>
    ///     字段名。
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    ///     寄存器地址。
    /// </summary>
    public string Register { get; set; } = string.Empty;

    /// <summary>
    ///     数据类型。
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    ///     批量读取时的偏移索引。
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    ///     字符串字节长度，仅字符串读取时使用。
    /// </summary>
    public int StringByteLength { get; set; }

    /// <summary>
    ///     字符串编码。
    /// </summary>
    public string Encoding { get; set; } = string.Empty;

    /// <summary>
    ///     数值转换表达式。
    /// </summary>
    public string EvalExpression { get; set; } = string.Empty;
}
