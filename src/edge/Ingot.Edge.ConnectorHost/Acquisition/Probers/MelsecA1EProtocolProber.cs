// MELSEC 1E 协议探查。
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class MelsecA1EProtocolProber(
    AcquisitionHttpEgressPolicy httpEgressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.MelsecA1E;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.MelsecA1E
            ?? throw new InvalidOperationException("MELSEC 1E 连接配置不能为空。");
        using var client = await httpEgressPolicy.ConnectTcpAsync(
            connection.Host,
            connection.Port,
            "MELSEC 1E",
            deployment.Task.Execution.TimeoutMs,
            ct).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var selectors = MelsecA1EAcquisitionRunner.BuildSelectors(deployment);
        var plan = MelsecA1EAcquisitionRunner.BuildReadPlan(
            selectors,
            connection.MaxMergeGap);
        var values = await MelsecA1EAcquisitionRunner.ReadSnapshotAsync(
            stream,
            connection,
            plan,
            ct).ConfigureAwait(false);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "register", mappingsValidated);
    }
}
