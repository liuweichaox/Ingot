// Probes only declared MELSEC MC 3E points through the shared, allowlisted TCP session.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

/// <summary>Validates configured MC 3E points without address-space discovery.</summary>
public sealed class MelsecMc3EProtocolProber(
    AcquisitionHttpEgressPolicy egressPolicy,
    RegisterAcquisitionSessionPool? sessionPool = null) : IProtocolProber
{
    private readonly RegisterAcquisitionSessionPool _sessionPool = sessionPool ?? new();

    public string Protocol => AcquisitionProtocols.MelsecMc3E;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
            throw new InvalidOperationException("MELSEC MC 3E 不支持自动地址扫描；请先填写已知 PLC 点位后再探查。");

        var task = deployment.Task;
        var connection = task.MelsecMc3E
            ?? throw new InvalidOperationException("MELSEC MC 3E 连接配置不能为空。");
        var selectors = MelsecMc3EProtocolSupport.BuildSelectors(deployment);
        var plan = MelsecMc3EProtocolSupport.BuildReadPlan(selectors, connection.MaxMergeGap);
        await using var lease = await _sessionPool.AcquireAsync(
            AcquisitionProtocols.MelsecMc3E,
            connection.Host,
            connection.Port,
            "binary",
            task.DataSourceId,
            task.DataSourceVersion,
            RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, task),
            token => egressPolicy.ConnectTcpAsync(
                connection.Host,
                connection.Port,
                "MELSEC MC 3E",
                task.Execution.TimeoutMs,
                token),
            ct).ConfigureAwait(false);
        var values = await lease.ExecuteMelsecMc3EAsync(
            task.Execution.TimeoutMs,
            (stream, readToken) => MelsecMc3EProtocolSupport.ReadSnapshotAsync(
                stream,
                connection,
                plan,
                readToken),
            ct).ConfigureAwait(false);

        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "melsec-mc-3e-device", mappingsValidated);
    }
}
