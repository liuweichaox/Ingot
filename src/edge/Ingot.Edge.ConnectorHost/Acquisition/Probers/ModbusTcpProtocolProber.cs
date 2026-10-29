// Modbus TCP 协议探查通过共享会话读取一份完整寄存器快照。
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class ModbusTcpProtocolProber(
    AcquisitionHttpEgressPolicy httpEgressPolicy,
    RegisterAcquisitionSessionPool? sessionPool = null) : IProtocolProber
{
    private readonly RegisterAcquisitionSessionPool _sessionPool = sessionPool ?? new();

    public string Protocol => AcquisitionProtocols.ModbusTcp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.ModbusTcp
            ?? throw new InvalidOperationException("Modbus TCP 连接配置不能为空。");
        var task = deployment.Task;
        var selectors = ModbusTcpAcquisitionRunner.BuildSelectors(deployment, connection.AddressBase);
        await using var lease = await _sessionPool.AcquireAsync(
            AcquisitionProtocols.ModbusTcp,
            connection.Host,
            connection.Port,
            string.Empty,
            task.DataSourceId,
            task.DataSourceVersion,
            RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, task),
            token => httpEgressPolicy.ConnectTcpAsync(
                connection.Host,
                connection.Port,
                "Modbus TCP",
                task.Execution.TimeoutMs,
                token),
            ct).ConfigureAwait(false);
        var values = await lease.ExecuteModbusAsync(
            task.Execution.TimeoutMs,
            (master, readToken) => ModbusTcpAcquisitionRunner.ReadSnapshotAsync(
                master,
                connection.UnitId,
                selectors,
                connection.MaxMergeGap,
                readToken),
            ct).ConfigureAwait(false);

        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "register", mappingsValidated);
    }
}
