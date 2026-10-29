// Verifies only configured EtherNet/IP tags; automatic tag browsing and PLC writes are not exposed.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class EtherNetIpProtocolProber(AcquisitionHttpEgressPolicy egressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.EtherNetIp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken cancellationToken)
    {
        var connection = deployment.Task.EtherNetIp
            ?? throw new InvalidOperationException("EtherNet/IP 连接配置不能为空。");
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
            throw new InvalidOperationException("EtherNet/IP 不支持自动标签扫描；请先配置已知标签后再探查。");

        var selectors = EtherNetIpProtocolSupport.BuildSelectors(deployment);
        using var session = await EtherNetIpProtocolSupport.OpenAsync(
            connection,
            selectors,
            egressPolicy,
            deployment.Task.Execution.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
        var values = await session.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "ethernet-ip-tag", mappingsValidated);
    }
}
