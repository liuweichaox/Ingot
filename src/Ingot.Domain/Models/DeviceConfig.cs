using System.Collections.Generic;
using Ingot.Domain.Events;

namespace Ingot.Domain.Models;

/// <summary>
///     采集表配置
/// </summary>
public class DeviceConfig
{
    private string _sourceCode = string.Empty;

    /// <summary>
    ///     配置结构版本。
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    ///     是否启用
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    ///     源编码。SchemaVersion 2 的源中立名称。
    /// </summary>
    public string SourceCode
    {
        get => _sourceCode;
        set => _sourceCode = value?.Trim() ?? string.Empty;
    }

    /// <summary>
    ///     v1 兼容字段。永久作为 SourceCode 的别名保留。
    /// </summary>
    public string PlcCode
    {
        get => _sourceCode;
        set => _sourceCode = value?.Trim() ?? string.Empty;
    }

    /// <summary>源适配器类型。当前默认且唯一实现为 plc。</summary>
    public string Adapter { get; set; } = "plc";

    /// <summary>行业 Profile 名称。</summary>
    public string Profile { get; set; } = "core";

    /// <summary>当规则未显式声明 Subject 时使用的默认资产。</summary>
    public ObjectRef? Asset { get; set; }

    /// <summary>
    ///     主机地址，可以是 IP 或主机名。
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    ///     端口
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    ///     驱动标识。配置只认完整 Driver 名称，例如：melsec-a1e。
    /// </summary>
    public string? Driver { get; set; }

    /// <summary>
    ///     协议附加参数。不同驱动可按需读取自身关心的选项。
    /// </summary>
    public Dictionary<string, string> ProtocolOptions { get; set; } = new();

    /// <summary>
    ///     心跳检测地址
    /// </summary>
    public string HeartbeatMonitorRegister { get; set; } = string.Empty;

    /// <summary>
    ///     心跳检测间隔时间（ms）
    /// </summary>
    public int HeartbeatPollingInterval { get; set; }

    /// <summary>
    ///     采集通道
    /// </summary>
    public List<AcquisitionChannel> Channels { get; set; } = new();

    /// <summary>与遥测通道并列的生产事件派生规则。</summary>
    public List<EventRule> EventRules { get; set; } = new();
}
