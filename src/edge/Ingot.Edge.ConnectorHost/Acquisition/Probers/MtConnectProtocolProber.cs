// Discovers MTConnect DataItems and previews only the observation IDs already selected by the task.
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

internal sealed class MtConnectProtocolProber : IProtocolProber
{
    private readonly MtConnectTransport _transport;

    public MtConnectProtocolProber(MtConnectTransport transport) => _transport = transport;

    public string Protocol => AcquisitionProtocols.MtConnect;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.MtConnect
            ?? throw new InvalidOperationException("MTConnect 连接配置不能为空。");
        var catalog = await _transport.ReadProbeAsync(connection, ct).ConfigureAwait(false);
        var configuredIds = MtConnectAcquisitionSupport.ConfiguredDataItemIds(deployment);
        var supportedIds = configuredIds
            .Where(id => catalog.DataItems.TryGetValue(id, out var item) && item.IsSupportedScalar)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var observations = supportedIds.Length == 0
            ? new Dictionary<string, MtConnectObservation>(StringComparer.Ordinal)
            : await _transport.ReadCurrentAsync(connection, catalog, supportedIds, ct).ConfigureAwait(false);

        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var id in configuredIds)
            raw[id] = observations.TryGetValue(id, out var observation) ? observation.Value : null;

        var points = catalog.DataItems.Values
            .OrderBy(static item => item.ComponentPath, StringComparer.Ordinal)
            .ThenBy(static item => item.Name, StringComparer.Ordinal)
            .Select(item => ToProbePoint(
                item,
                observations.GetValueOrDefault(item.Id),
                catalog.AgentVersion,
                catalog.DeviceName,
                catalog.DeviceUuid))
            .ToArray();
        var hasUnsupportedConfiguredPoint = configuredIds.Any(id =>
            !catalog.DataItems.TryGetValue(id, out var item) || !item.IsSupportedScalar);
        var mappingsValidated = !hasUnsupportedConfiguredPoint &&
                                AcquisitionProbeSupport.ValidateProtocolMapping(deployment, raw);
        return new ProbeSnapshot(raw, points, mappingsValidated);
    }

    private static AcquisitionProbePoint ToProbePoint(
        MtConnectDataItem item,
        MtConnectObservation? observation,
        string? agentVersion,
        string? deviceName,
        string? deviceUuid)
    {
        var supported = item.IsSupportedScalar;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["category"] = item.Category,
            ["type"] = item.Type,
            ["supported"] = supported ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(item.SubType)) metadata["subType"] = item.SubType;
        if (!string.IsNullOrWhiteSpace(item.ComponentPath)) metadata["componentPath"] = item.ComponentPath;
        if (!string.IsNullOrWhiteSpace(item.ComponentId)) metadata["componentId"] = item.ComponentId;
        if (!string.IsNullOrWhiteSpace(item.Units)) metadata["units"] = item.Units;
        if (!string.IsNullOrWhiteSpace(agentVersion)) metadata["agentVersion"] = agentVersion;
        if (!string.IsNullOrWhiteSpace(deviceName)) metadata["deviceName"] = deviceName;
        if (!string.IsNullOrWhiteSpace(deviceUuid)) metadata["deviceUuid"] = deviceUuid;
        if (!string.IsNullOrWhiteSpace(observation?.Sequence)) metadata["observationSequence"] = observation.Sequence;
        if (!supported)
            metadata["supportReason"] = item.Category == "CONDITION"
                ? "Condition 需要独立健康事件模型，当前不能映射为普通采样值。"
                : "当前只支持标量 SAMPLE 和 EVENT。";

        return new AcquisitionProbePoint
        {
            Path = item.Id,
            Name = item.Name,
            Kind = item.Category,
            DataType = supported ? "auto" : "unsupported",
            RawValue = AcquisitionProbeSupport.Format(observation?.Value),
            Unit = item.Units,
            Quality = observation?.Quality,
            SourceTimestamp = observation?.Timestamp,
            Metadata = metadata
        };
    }
}
