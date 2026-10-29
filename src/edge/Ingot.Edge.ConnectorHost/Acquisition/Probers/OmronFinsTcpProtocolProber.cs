// Probes only declared Omron FINS/TCP points through the shared, allowlisted session.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class OmronFinsTcpProtocolProber(
    AcquisitionHttpEgressPolicy egressPolicy,
    RegisterAcquisitionSessionPool? sessionPool = null) : IProtocolProber
{
    private readonly RegisterAcquisitionSessionPool _sessionPool = sessionPool ?? new();

    public string Protocol => AcquisitionProtocols.OmronFinsTcp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.OmronFinsTcp
            ?? throw new InvalidOperationException("Omron FINS/TCP 连接配置不能为空。");
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
            throw new InvalidOperationException("Omron FINS/TCP 不支持自动地址扫描；请先填写已知 PLC 点位后再探查。");

        var task = deployment.Task;
        var selectors = OmronFinsTcpProtocolSupport.BuildSelectors(deployment);
        await using var lease = await _sessionPool.AcquireAsync(
            AcquisitionProtocols.OmronFinsTcp,
            connection.Host,
            connection.Port,
            string.Empty,
            task.DataSourceId,
            task.DataSourceVersion,
            RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, task),
            token => egressPolicy.ConnectTcpAsync(
                connection.Host,
                connection.Port,
                "Omron FINS/TCP",
                task.Execution.TimeoutMs,
                token),
            ct).ConfigureAwait(false);
        var values = await lease.ExecuteOmronFinsTcpAsync(
            task.Execution.TimeoutMs,
            (session, readToken) => OmronFinsTcpProtocolSupport.ReadSnapshotAsync(session, selectors, readToken),
            ct).ConfigureAwait(false);

        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "omron-fins-address", mappingsValidated);
    }
}
