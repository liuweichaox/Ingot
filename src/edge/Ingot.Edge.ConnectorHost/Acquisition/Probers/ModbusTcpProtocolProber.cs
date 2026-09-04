// Modbus TCP 协议探查。
using Ingot.Contracts.Acquisition;
using NModbus;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class ModbusTcpProtocolProber(
    AcquisitionHttpEgressPolicy httpEgressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.ModbusTcp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.ModbusTcp
            ?? throw new InvalidOperationException("Modbus TCP 连接配置不能为空。");
        using var client = await httpEgressPolicy.ConnectTcpAsync(
            connection.Host,
            connection.Port,
            "Modbus TCP",
            deployment.Task.Execution.TimeoutMs,
            ct).ConfigureAwait(false);
        var factory = new ModbusFactory();
        using var master = factory.CreateMaster(client);
        var values = await ModbusTcpAcquisitionRunner.ReadSnapshotAsync(
            master,
            connection.UnitId,
            ModbusTcpAcquisitionRunner.BuildSelectors(deployment, connection.AddressBase),
            connection.MaxMergeGap,
            ct).ConfigureAwait(false);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "register", mappingsValidated);
    }
}
