// 各采集协议探查策略的统一边界。
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

/// <summary>对单一采集协议执行有界探查，并返回设备点位快照。</summary>
public interface IProtocolProber
{
    string Protocol { get; }

    Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken cancellationToken);
}
