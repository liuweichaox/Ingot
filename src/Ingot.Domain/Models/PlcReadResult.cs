using System;

namespace Ingot.Domain.Models;

/// <summary>
///     Plc 读取结果。
/// </summary>
public class PlcReadResult
{
    /// <summary>
    ///     读取是否成功。
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    ///     读取的原始内容。
    /// </summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>
    ///     结果描述信息。
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
