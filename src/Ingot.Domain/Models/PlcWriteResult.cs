namespace Ingot.Domain.Models;

/// <summary>
///     Plc 写入结果。
/// </summary>
public class PlcWriteResult
{
    /// <summary>
    ///     写入是否成功。
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    ///     结果描述信息。
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
