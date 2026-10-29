// Reject incompatible MQTT identities and serial bus settings before active sessions conflict.
namespace Ingot.Contracts.Acquisition;

public static class AcquisitionSessionCompatibility
{
    public static IReadOnlyList<AcquisitionValidationError> Validate(
        IngestionTask candidate,
        IEnumerable<IngestionTask> peers)
    {
        if (candidate.Protocol == AcquisitionProtocols.Mqtt && candidate.Mqtt is { } connection)
        {
            var clientId = EffectiveMqttClientId(candidate);
            foreach (var peer in peers)
            {
                if (peer.TaskId == candidate.TaskId || peer.EdgeId != candidate.EdgeId ||
                    peer.Protocol != AcquisitionProtocols.Mqtt || peer.Mqtt is null)
                    continue;
                if (connection.Port == peer.Mqtt.Port &&
                    string.Equals(connection.Host.Trim().TrimEnd('.'), peer.Mqtt.Host.Trim().TrimEnd('.'),
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(clientId, EffectiveMqttClientId(peer), StringComparison.Ordinal))
                {
                    return [new AcquisitionValidationError("mqtt.clientId",
                        $"MQTT ClientId 与任务 {peer.TaskId} 冲突；同一 Broker 上的独立采集组必须使用不同 ClientId，或留空由任务身份生成。")];
                }
            }
        }

        var serialConnection = GetSerialConnection(candidate);
        if (serialConnection is null) return [];
        var connectionOnCandidate = serialConnection.Value;
        foreach (var peer in peers)
        {
            if (peer.TaskId == candidate.TaskId || peer.EdgeId != candidate.EdgeId)
                continue;
            var peerConnection = GetSerialConnection(peer);
            if (peerConnection is null) continue;
            var peerPort = peerConnection.Value.PortName;
            var isComPort = IsComPort(connectionOnCandidate.PortName) && IsComPort(peerPort);
            var comparison = isComPort ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(connectionOnCandidate.PortName, peerPort, comparison)) continue;
            if (connectionOnCandidate != peerConnection.Value)
            {
                return [new AcquisitionValidationError($"{connectionOnCandidate.Section}.portName",
                    $"串口 {connectionOnCandidate.PortName} 已被任务 {peer.TaskId} 以不同协议格式或总线参数使用；同一物理串口必须共享兼容的帧格式、波特率和站点配置。")];
            }
        }
        return [];
    }

    private static SerialConnection?
        GetSerialConnection(IngestionTask task)
    {
        return task.Protocol switch
        {
            AcquisitionProtocols.ModbusAscii when task.ModbusAscii is { } value => new SerialConnection(
                NormalizePortName(value.PortName),
                "ascii",
                value.BaudRate,
                value.DataBits,
                value.Parity,
                value.StopBits,
                string.Empty,
                "modbusAscii"),
            AcquisitionProtocols.ModbusRtu when task.ModbusRtu is { } value => new SerialConnection(
                NormalizePortName(value.PortName),
                "rtu",
                value.BaudRate,
                value.DataBits,
                value.Parity,
                value.StopBits,
                string.Empty,
                "modbusRtu"),
            AcquisitionProtocols.BacnetMstp when task.BacnetMstp is { } value => new SerialConnection(
                NormalizePortName(value.PortName),
                "bacnet-mstp",
                value.BaudRate,
                8,
                "none",
                "one",
                $"{value.LocalStationAddress}:{value.MaxMaster}:{value.MaxInfoFrames}",
                "bacnetMstp"),
            _ => null
        };
    }

    private readonly record struct SerialConnection(
        string PortName,
        string Framing,
        int BaudRate,
        int DataBits,
        string Parity,
        string StopBits,
        string BusIdentity,
        string Section);

    private static string NormalizePortName(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("\\\\.\\", StringComparison.Ordinal))
            normalized = normalized[4..];
        return IsComPort(normalized) ? normalized.ToUpperInvariant() : normalized;
    }

    private static bool IsComPort(string value)
        => System.Text.RegularExpressions.Regex.IsMatch(
            value,
            @"^(?:COM\d+|\\\\\.\\COM\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string EffectiveMqttClientId(IngestionTask task)
        => string.IsNullOrWhiteSpace(task.Mqtt?.ClientId)
            ? $"ingot-{task.EdgeId}-{task.TaskId}"
            : task.Mqtt.ClientId;
}
