// Verify configured Siemens S7 addresses without scanning or writing PLC memory.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class SiemensS7ProtocolProber(AcquisitionHttpEgressPolicy egressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.SiemensS7;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken cancellationToken)
    {
        var connection = deployment.Task.SiemensS7
            ?? throw new InvalidOperationException("Siemens S7 连接配置不能为空。");
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
            throw new InvalidOperationException("Siemens S7 不支持自动地址扫描；请配置已知地址后再探查。");

        var selectors = SiemensS7ProtocolSupport.BuildSelectors(deployment);
        if (selectors.Count == 0)
            throw new InvalidOperationException("Siemens S7 探查至少需要一个已配置地址。");

        using var plc = await SiemensS7ProtocolSupport.OpenAsync(
            connection,
            egressPolicy,
            deployment.Task.Execution.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
        var values = await SiemensS7ProtocolSupport.ReadSnapshotAsync(
            plc,
            selectors,
            connection.MaxMergeGap,
            deployment.Task.Execution.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "s7-address", mappingsValidated);
    }
}
