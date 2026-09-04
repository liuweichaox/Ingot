// OPC UA 协议探查。
using Ingot.Contracts.Acquisition;
using Opc.Ua;
using Opc.Ua.Client;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class OpcUaProtocolProber(
    IAcquisitionSecretResolver secrets,
    AcquisitionHttpEgressPolicy httpEgressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.OpcUa;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discoveryQuery,
        CancellationToken ct)
    {
        var connection = deployment.Task.OpcUa
            ?? throw new InvalidOperationException("OPC UA 连接配置不能为空。");
        var discoveryUri = await httpEgressPolicy.ResolvePinnedEndpointAsync(
            new Uri(connection.EndpointUrl),
            "OPC UA",
            ct,
            AcquisitionProbeSupport.UsesCredentials(connection)).ConfigureAwait(false);
        var configuration = await OpcUaAcquisitionRunner.CreateConfigurationAsync(connection, secrets, ct)
            .ConfigureAwait(false);
        var sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(_ => { }));
        using var discovery = await DiscoveryClient.CreateAsync(
            configuration,
            discoveryUri,
            DiagnosticsMasks.None,
            ct).ConfigureAwait(false);
        var endpoints = await discovery.GetEndpointsAsync(null, ct).ConfigureAwait(false);
        var securityMode = connection.SecurityMode switch
        {
            "sign" => MessageSecurityMode.Sign,
            "sign-and-encrypt" => MessageSecurityMode.SignAndEncrypt,
            _ => MessageSecurityMode.None
        };
        var securityPolicy = connection.SecurityPolicy switch
        {
            "Basic256Sha256" => SecurityPolicies.Basic256Sha256,
            "Aes128_Sha256_RsaOaep" => SecurityPolicies.Aes128_Sha256_RsaOaep,
            "Aes256_Sha256_RsaPss" => SecurityPolicies.Aes256_Sha256_RsaPss,
            _ => SecurityPolicies.None
        };
        var selected = endpoints.FirstOrDefault(item =>
            item.SecurityMode == securityMode && item.SecurityPolicyUri == securityPolicy)
            ?? throw new InvalidOperationException("OPC UA 服务器不提供所选安全组合。");
        selected.EndpointUrl = (await httpEgressPolicy.ResolvePinnedEndpointAsync(
            new Uri(selected.EndpointUrl),
            "OPC UA",
            ct,
            AcquisitionProbeSupport.UsesCredentials(connection)).ConfigureAwait(false)).ToString();
        var endpoint = new ConfiguredEndpoint(
            null,
            selected,
            EndpointConfiguration.Create(configuration));
        using var session = await sessionFactory.CreateAsync(
            configuration,
            endpoint,
            false,
            $"Ingot probe {deployment.Task.TaskId}",
            (uint)Math.Clamp(deployment.Task.Execution.TimeoutMs, 1000, 30_000),
            OpcUaAcquisitionRunner.CreateIdentity(connection, secrets),
            null,
            ct).ConfigureAwait(false);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var points = new List<AcquisitionProbePoint>();
        AcquisitionProbeSupport.BrowseOpcNodes(session, ObjectIds.ObjectsFolder, string.Empty, 0, values, points);
        var page = AcquisitionProbeSupport.ApplyDiscoveryQuery(points, discoveryQuery);
#pragma warning disable CS0618
        var mappedValues = deployment.Task.ValueMappings
            .Concat(deployment.Task.ProcessSpecification?.ParameterMappings ?? [])
            .GroupBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var pathsToRead = AcquisitionProbeSupport.MappedPaths(deployment)
            .Concat(page.Points.Select(static item => item.Path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var readValues = new Dictionary<string, DataValue>(StringComparer.Ordinal);
        foreach (var path in pathsToRead)
        {
            var value = session.ReadValue(NodeId.Parse(path));
            readValues[path] = value;
            var accepted = mappedValues.TryGetValue(path, out var mappings) &&
                           mappings.Any(mapping =>
                               !string.IsNullOrWhiteSpace(mapping.QualityPath) &&
                               mapping.AcceptedQualityValues.Contains(
                                   value.StatusCode.ToString(), StringComparer.OrdinalIgnoreCase));
            values[path] = StatusCode.IsGood(value.StatusCode) || accepted ? value.Value : null;
            if (!points.Any(point => point.Path == path))
            {
                AcquisitionProbeSupport.AddOpcPoint(points, path, path, value);
            }
        }
        foreach (var mapping in deployment.Task.ValueMappings
                     .Concat(deployment.Task.ProcessSpecification?.ParameterMappings ?? [])
                     .Where(static item => !string.IsNullOrWhiteSpace(item.QualityPath)))
        {
            var value = session.ReadValue(NodeId.Parse(mapping.SourcePath));
            values[mapping.QualityPath == "$status"
                ? $"$status:{mapping.SourcePath}"
                : mapping.QualityPath!] = value.StatusCode.ToString();
        }
#pragma warning restore CS0618
        var mappingsValidated = AcquisitionProbeSupport.ValidateProtocolMapping(deployment, values);
        var hydratedPage = page with
        {
            Points = page.Points.Select(point => readValues.TryGetValue(point.Path, out var value)
                ? point with
                {
                    DataType = value.Value?.GetType().Name ?? "null",
                    RawValue = AcquisitionProbeSupport.Format(value.Value),
                    Quality = value.StatusCode.ToString(),
                    SourceTimestamp = value.SourceTimestamp == DateTime.MinValue
                        ? null
                        : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc))
                }
                : point).ToArray()
        };
        return new ProbeSnapshot(values, points, mappingsValidated, hydratedPage);
    }
}
