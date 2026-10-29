// Modbus ASCII 探查仅回读已配置寄存器，并使用正式采集共享串口会话。
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class ModbusAsciiProtocolProber(ModbusSerialSessionPool sessionPool) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.ModbusAscii;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.ModbusAscii
            ?? throw new InvalidOperationException("Modbus ASCII 串口配置不能为空。");
        var task = deployment.Task;
        var selectors = ModbusTcpAcquisitionRunner.BuildSelectors(deployment, connection.AddressBase);
        await using var lease = await sessionPool.AcquireAsync(
            connection,
            task.DataSourceId,
            task.DataSourceVersion,
            ModbusSerialSessionPool.CreateSourceVersionFingerprint(connection, task),
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
