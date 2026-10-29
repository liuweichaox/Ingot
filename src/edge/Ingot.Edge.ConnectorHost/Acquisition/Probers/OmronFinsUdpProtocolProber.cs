// Probes only declared Omron FINS/UDP points over an allowlisted direct unicast target.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class OmronFinsUdpProtocolProber(AcquisitionHttpEgressPolicy egressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.OmronFinsUdp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.OmronFinsUdp
            ?? throw new InvalidOperationException("Omron FINS/UDP 连接配置不能为空。");
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
            throw new InvalidOperationException("Omron FINS/UDP 不支持地址扫描；请先填写已知 PLC 点位后再探查。");

        var selectors = OmronFinsUdpProtocolSupport.BuildSelectors(deployment);
        if (selectors.Count == 0)
            throw new InvalidOperationException("Omron FINS/UDP 探查至少需要一个已配置点位。");

        var client = await egressPolicy.ConnectUdpAsync(
            connection.Host,
            connection.Port,
            "Omron FINS/UDP",
            task.Execution.TimeoutMs,
            ct).ConfigureAwait(false);
        using var session = new OmronFinsUdpSession(client, connection.ClientNodeAddress, connection.ServerNodeAddress);
        var values = await OmronFinsUdpProtocolSupport.ReadSnapshotAsync(
            session,
            selectors,
            task.Execution.TimeoutMs,
            ct).ConfigureAwait(false);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "omron-fins-address", mappingsValidated);
    }
}
