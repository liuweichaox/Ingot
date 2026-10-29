// MELSEC 1E 协议探查通过共享会话读取一份完整寄存器快照。
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class MelsecA1EProtocolProber(
    AcquisitionHttpEgressPolicy httpEgressPolicy,
    RegisterAcquisitionSessionPool? sessionPool = null) : IProtocolProber
{
    private readonly RegisterAcquisitionSessionPool _sessionPool = sessionPool ?? new();

    public string Protocol => AcquisitionProtocols.MelsecA1E;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.MelsecA1E
            ?? throw new InvalidOperationException("MELSEC 1E 连接配置不能为空。");
        var task = deployment.Task;
        var selectors = MelsecA1EAcquisitionRunner.BuildSelectors(deployment);
        var plan = MelsecA1EAcquisitionRunner.BuildReadPlan(
            selectors,
            connection.MaxMergeGap);
        await using var lease = await _sessionPool.AcquireAsync(
            AcquisitionProtocols.MelsecA1E,
            connection.Host,
            connection.Port,
            connection.DataCode,
            task.DataSourceId,
            task.DataSourceVersion,
            RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, task),
            token => httpEgressPolicy.ConnectTcpAsync(
                connection.Host,
                connection.Port,
                "MELSEC 1E",
                task.Execution.TimeoutMs,
                token),
            ct).ConfigureAwait(false);
        var values = await lease.ExecuteMelsecAsync(
            task.Execution.TimeoutMs,
            (stream, readToken) => MelsecA1EAcquisitionRunner.ReadSnapshotAsync(
                stream,
                connection,
                plan,
                readToken),
            ct).ConfigureAwait(false);

        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return AcquisitionProbeSupport.FromRegisterValues(values, "register", mappingsValidated);
    }
}
