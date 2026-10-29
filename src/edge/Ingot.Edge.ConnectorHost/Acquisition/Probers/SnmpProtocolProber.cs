// Discovers a bounded numeric OID subtree and previews only explicitly configured SNMP mappings.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class SnmpProtocolProber(
    AcquisitionHttpEgressPolicy egressPolicy,
    IAcquisitionSecretResolver secretResolver,
    ILogger<SnmpProtocolProber> logger) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.Snmp;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.Snmp
            ?? throw new InvalidOperationException("SNMP 连接配置不能为空。");
        var timeoutMs = Math.Clamp(task.Execution.TimeoutMs, 100, 30_000);
        using var session = await SnmpProtocolSupport.OpenAsync(
            connection,
            secretResolver,
            egressPolicy,
            timeoutMs,
            logger,
            ct).ConfigureAwait(false);

        var browseRoot = SnmpProtocolSupport.ResolveBrowseRoot(connection, discovery);
        var points = await session.BrowseAsync(browseRoot, timeoutMs, ct).ConfigureAwait(false);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
        {
            var selectors = SnmpProtocolSupport.ConfiguredOids(deployment);
            if (selectors.Count > 0)
            {
                var readValues = await session.ReadSnapshotAsync(
                    selectors,
                    SnmpProtocolSupport.ConfiguredDataTypes(deployment),
                    ct).ConfigureAwait(false);
                foreach (var value in readValues) values[value.Key] = value.Value;
            }
        }

        var pageQuery = string.IsNullOrWhiteSpace(discovery.RootPath)
            ? discovery
            : discovery with
            {
                RootPath = AcquisitionSelectors.TryParseSnmpOid(
                    discovery.RootPath,
                    out var requestedRoot,
                    out _)
                    ? requestedRoot.Value
                    : discovery.RootPath
            };
        var page = AcquisitionProbeSupport.ApplyDiscoveryQuery(points, pageQuery);
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        return new ProbeSnapshot(values, points, mappingsValidated, page);
    }
}
