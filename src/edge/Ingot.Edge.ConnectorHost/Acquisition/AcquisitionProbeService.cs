using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Acquisition;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using NModbus;
using Opc.Ua;
using Opc.Ua.Client;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionProbeService(
    IHttpClientFactory httpClientFactory,
    IAcquisitionSecretResolver secrets)
{
    private const int MaximumPoints = 20_000;

    public async Task<AcquisitionProbeResult> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery? discovery,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
            deployment.Task.Execution.TimeoutMs,
            500,
            30_000));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var ct = timeoutSource.Token;

        discovery ??= new SourceDiscoveryQuery();
        var raw = deployment.Task.Protocol switch
        {
            AcquisitionProtocols.HttpPolling => await ProbeHttpAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.Mqtt => await ProbeMqttAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.OpcUa => await ProbeOpcUaAsync(deployment, discovery, ct).ConfigureAwait(false),
            AcquisitionProtocols.ModbusTcp => await ProbeModbusAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.MelsecA1E => await ProbeMelsecAsync(deployment, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"不支持采集协议 {deployment.Task.Protocol}。")
        };

        var page = raw.Page ?? ApplyDiscoveryQuery(raw.Points, discovery);
        var previews = BuildPreviews(deployment, raw.Values, raw.TopicValues);
        var missing = previews.Where(static item => !item.Accepted).ToArray();
        var unlocated = PublicationEvidencePaths(deployment)
            .Where(path => !EvidenceLocated(deployment.Task.Protocol, path, raw.Values, raw.TopicValues))
            .ToArray();
        var warnings = unlocated
            .Select(path => $"已配置设备路径未在探查样本中出现：{path.Display}。")
            .ToArray();
        var allMappingsLocated = unlocated.Length == 0;
        return new AcquisitionProbeResult
        {
            Success = missing.Length == 0 && allMappingsLocated && raw.MappingsValidated,
            MappingsValidated = missing.Length == 0 && allMappingsLocated && raw.MappingsValidated,
            Protocol = deployment.Task.Protocol,
            TestedAt = DateTimeOffset.UtcNow,
            Message = missing.Length == 0 && allMappingsLocated && raw.MappingsValidated
                ? $"连接成功，读取到 {raw.Points.Count} 个设备点位，映射验证通过。"
                : missing.Length > 0
                    ? $"连接成功，但有 {missing.Length} 个必需映射未读取到值。"
                    : !allMappingsLocated
                        ? $"连接成功，但有 {unlocated.Length} 个已配置设备路径未在探查样本中出现。"
                    : "连接成功，但设备报文未通过映射验证。",
            Points = page.Points,
            NextCursor = page.NextCursor,
            ScannedPointCount = raw.Points.Count,
            ScanLimitReached = raw.Points.Count >= MaximumPoints,
            Mappings = previews,
            Warnings = warnings
        };
    }

    private async Task<ProbeSnapshot> ProbeHttpAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Task.HttpPolling;
        var requestUri = HttpAcquisitionRequestFactory.CreateEndpoint(
            connection.BaseUrl, connection.SnapshotPath);
        using var request = HttpAcquisitionRequestFactory.Create(
            requestUri,
            connection.Method,
            connection.RequestBody,
            connection.ContentType,
            connection.Headers,
            connection.HeaderSecretRefs,
            secrets);
        using var response = await httpClientFactory.CreateClient("device-http-acquisition")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var snapshot = await HttpJsonSnapshotReader.ReadAsync(response.Content, ct).ConfigureAwait(false);
        var mappingsValidated = true;
        if (!IsDiscoveryProbe(deployment))
        {
            try
            {
                HttpPollingSnapshotMapper.Map(
                    snapshot,
                    JsonAcquisitionOptionsFactory.Create(deployment),
                    deployment.Task.Source,
                    previousProcessSpecificationIdentity: null);
            }
            catch (InvalidDataException)
            {
                mappingsValidated = false;
            }
        }
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var points = new List<AcquisitionProbePoint>();
        FlattenJson(snapshot, string.Empty, values, points);
        return new ProbeSnapshot(values, points, mappingsValidated);
    }

    private async Task<ProbeSnapshot> ProbeMqttAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Task.Mqtt
            ?? throw new InvalidOperationException("MQTT 连接配置不能为空。");
        if (connection.Topics.Count == 0)
            throw new InvalidOperationException("MQTT 至少需要一个订阅主题。");

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        var discoveryProbe = IsDiscoveryProbe(deployment);
        var discoverySnapshots = discoveryProbe ? new MqttSnapshotAccumulator(connection.Topics) : null;
        var assembler = discoveryProbe
            ? null
            : new MqttSnapshotAssembler(
                MqttSnapshotAssembler.SlotsFor(deployment.Task),
                connection.SnapshotMaxAgeSeconds);
        var jsonOptions = JsonAcquisitionOptionsFactory.Create(deployment);
        var sample = new TaskCompletionSource<ProbeSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gate = new SemaphoreSlim(1, 1);
        client.ApplicationMessageReceivedAsync += async message =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var topic = message.ApplicationMessage.Topic;
                var receivedAt = DateTimeOffset.UtcNow;
                var subscription = MqttSnapshotAssembler.SubscriptionFor(connection.Topics, topic);
                var decodedPayload = MqttPayloadDecoder.Decode(message.ApplicationMessage.Payload, connection);
                if (discoverySnapshots is not null)
                {
                    using var discovered = discoverySnapshots.Add(
                        topic,
                        decodedPayload,
                        receivedAt,
                        connection.SnapshotMaxAgeSeconds,
                        connection.SnapshotMaxAgeSeconds);
                    if (!discovered.IsComplete || !discovered.IsCoherent)
                        return;
                    var discoveredValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var discoveredTopicValues = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
                    var discoveredPoints = new List<AcquisitionProbePoint>();
                    FlattenJson(discovered.Aggregate.RootElement, string.Empty, discoveredValues, []);
                    foreach (var topicSnapshot in discovered.TopicSnapshots)
                    {
                        var topicValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                        FlattenJson(
                            topicSnapshot.Value,
                            string.Empty,
                            topicValues,
                            discoveredPoints,
                            topicSnapshot.Key);
                        discoveredTopicValues[topicSnapshot.Key] = topicValues;
                    }
                    sample.TrySetResult(new ProbeSnapshot(
                        discoveredValues,
                        discoveredPoints,
                        TopicValuesSource: discoveredTopicValues));
                    return;
                }

                using var document = JsonDocument.Parse(decodedPayload, AcquisitionJsonLimits.DocumentOptions);
                var payload = MqttSnapshotAssembler.Unwrap(document.RootElement, subscription?.PayloadRoot);
                assembler!.Ingest(
                    topic,
                    payload,
                    receivedAt,
                    MqttTopicVariableResolver.Resolve(subscription, topic));
                if (!assembler.TryBuildSnapshot(receivedAt, out var snapshot, out _))
                    return;
                using (snapshot)
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var topicValues = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
                    var points = new List<AcquisitionProbePoint>();
                    FlattenJson(snapshot!.RootElement, string.Empty, values, []);
                    var topicSnapshots = assembler.BuildTopicSnapshots(receivedAt);
                    if (topicSnapshots.Count == 0)
                        FlattenJson(snapshot.RootElement, string.Empty, new Dictionary<string, object?>(), points);
                    else
                        foreach (var topicSnapshot in topicSnapshots)
                        {
                            var isolated = new Dictionary<string, object?>(StringComparer.Ordinal);
                            FlattenJson(
                                topicSnapshot.Value,
                                string.Empty,
                                isolated,
                                points,
                                topicSnapshot.Key);
                            topicValues[topicSnapshot.Key] = isolated;
                        }
                    var mappingsValidated = true;
                    try
                    {
                        HttpPollingSnapshotMapper.Map(
                            snapshot.RootElement,
                            jsonOptions,
                            deployment.Task.Source,
                            previousProcessSpecificationIdentity: null,
                            topicSnapshots);
                    }
                    catch (InvalidDataException)
                    {
                        mappingsValidated = false;
                    }

                    sample.TrySetResult(new ProbeSnapshot(
                        values,
                        points,
                        mappingsValidated,
                        TopicValuesSource: topicValues));
                }
            }
            catch (Exception exception)
            {
                sample.TrySetException(exception);
            }
            finally
            {
                gate.Release();
            }
        };
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(connection.Host, connection.Port)
            .WithClientId(string.IsNullOrWhiteSpace(connection.ClientId)
                ? $"ingot-probe-{Guid.NewGuid():N}"
                : $"{connection.ClientId}-probe")
            .WithProtocolVersion(connection.ProtocolVersion == "3.1.1"
                ? MqttProtocolVersion.V311
                : MqttProtocolVersion.V500)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(connection.KeepAliveSeconds))
            .WithCleanSession(true);
        var password = AcquisitionSecretReference.ResolveOptional(
            secrets, connection.PasswordSecretRef, "MQTT 密码");
        if (!string.IsNullOrWhiteSpace(connection.Username))
            optionsBuilder.WithCredentials(connection.Username, password);
        if (connection.UseTls)
        {
            optionsBuilder.WithTlsOptions(options =>
            {
                options.UseTls().WithTargetHost(connection.Host);
                if (!string.IsNullOrWhiteSpace(connection.CaCertificatePath))
                {
                    var authority = X509CertificateLoader.LoadCertificateFromFile(connection.CaCertificatePath);
                    options.WithTrustChain(new X509Certificate2Collection(authority));
                }
                if (!string.IsNullOrWhiteSpace(connection.ClientCertificatePath))
                {
                    var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                        connection.ClientCertificatePath,
                        AcquisitionSecretReference.ResolveOptional(
                            secrets, connection.ClientCertificatePasswordSecretRef, "MQTT 客户端证书密码"));
                    options.WithClientCertificates([certificate]);
                }
            });
        }
        await client.ConnectAsync(optionsBuilder.Build(), ct).ConfigureAwait(false);
        foreach (var topic in connection.Topics)
        {
            var subscription = factory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter(filter => filter
                    .WithTopic(topic.Topic)
                    .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)topic.Qos))
                .Build();
            var subscribeResult = await client.SubscribeAsync(subscription, ct).ConfigureAwait(false);
            MqttSubscriptionGuard.EnsureAccepted(subscribeResult, topic.Topic);
        }
        var result = await sample.Task.WaitAsync(ct).ConfigureAwait(false);
        await client.DisconnectAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task<ProbeSnapshot> ProbeModbusAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Task.ModbusTcp
            ?? throw new InvalidOperationException("Modbus TCP 连接配置不能为空。");
        using var client = new TcpClient();
        await client.ConnectAsync(connection.Host, connection.Port, ct).ConfigureAwait(false);
        var factory = new ModbusFactory();
        using var master = factory.CreateMaster(client);
        var values = await ModbusTcpAcquisitionRunner.ReadSnapshotAsync(
            master,
            connection.UnitId,
            ModbusTcpAcquisitionRunner.BuildSelectors(deployment, connection.AddressBase),
            connection.MaxMergeGap,
            ct).ConfigureAwait(false);
        var mappingsValidated = ValidateProtocolMapping(deployment, values);
        return FromRegisterValues(values, "register", mappingsValidated);
    }

    private static async Task<ProbeSnapshot> ProbeMelsecAsync(
        AcquisitionDeployment deployment,
        CancellationToken ct)
    {
        var connection = deployment.Task.MelsecA1E
            ?? throw new InvalidOperationException("MELSEC 1E 连接配置不能为空。");
        using var client = new TcpClient();
        await client.ConnectAsync(connection.Host, connection.Port, ct).ConfigureAwait(false);
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
        var mappingsValidated = ValidateProtocolMapping(deployment, values);
        return FromRegisterValues(values, "register", mappingsValidated);
    }

    private async Task<ProbeSnapshot> ProbeOpcUaAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discoveryQuery,
        CancellationToken ct)
    {
        var connection = deployment.Task.OpcUa
            ?? throw new InvalidOperationException("OPC UA 连接配置不能为空。");
        var configuration = await OpcUaAcquisitionRunner.CreateConfigurationAsync(connection, secrets, ct)
            .ConfigureAwait(false);
        var sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(_ => { }));
        using var discovery = await DiscoveryClient.CreateAsync(
            configuration,
            new Uri(connection.EndpointUrl),
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
        BrowseOpcNodes(session, ObjectIds.ObjectsFolder, string.Empty, 0, values, points);
        var page = ApplyDiscoveryQuery(points, discoveryQuery);
#pragma warning disable CS0618
        var mappedValues = deployment.Task.ValueMappings
            .Concat(deployment.Task.ProcessSpecification?.ParameterMappings ?? [])
            .GroupBy(static item => item.SourcePath, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var pathsToRead = MappedPaths(deployment)
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
                AddOpcPoint(points, path, path, value);
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
        var mappingsValidated = ValidateProtocolMapping(deployment, values);
        var hydratedPage = page with
        {
            Points = page.Points.Select(point => readValues.TryGetValue(point.Path, out var value)
                ? point with
                {
                    DataType = value.Value?.GetType().Name ?? "null",
                    RawValue = Format(value.Value),
                    Quality = value.StatusCode.ToString(),
                    SourceTimestamp = value.SourceTimestamp == DateTime.MinValue
                        ? null
                        : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc))
                }
                : point).ToArray()
        };
        return new ProbeSnapshot(values, points, mappingsValidated, hydratedPage);
    }

    private static bool IsDiscoveryProbe(AcquisitionDeployment deployment)
        => deployment.Task.ValueMappings.Any(item =>
            item.SourcePath == "__probe_only__" && !item.Required);

    private static bool ValidateProtocolMapping(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> values)
    {
        if (IsDiscoveryProbe(deployment))
            return true;

        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            if (deployment.Task.TimestampMode == "source" &&
                !string.IsNullOrWhiteSpace(deployment.Task.TimestampPath))
            {
                if (!values.TryGetValue(deployment.Task.TimestampPath, out var rawTimestamp) ||
                    rawTimestamp is null)
                {
                    throw new InvalidDataException(
                        $"配置的时间来源没有读到值：{deployment.Task.TimestampPath}。");
                }
                occurredAt = AcquisitionTimestampParser.Parse(
                    rawTimestamp,
                    deployment.Task.TimestampEncoding,
                    deployment.Task.TimestampPath,
                    occurredAt,
                    deployment.Task.Execution.MaximumFutureTimestampSkewMs);
            }

            ProtocolAcquisitionSnapshotMapper.Map(
                deployment,
                values,
                deployment.Task.Source,
                previousProcessSpecificationIdentity: null,
                occurredAt: occurredAt);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

#pragma warning disable CS0618
    private static void BrowseOpcNodes(
        Opc.Ua.Client.ISession session,
        NodeId parent,
        string parentName,
        int depth,
        IDictionary<string, object?> values,
        ICollection<AcquisitionProbePoint> points,
        ISet<NodeId>? visited = null)
    {
        visited ??= new HashSet<NodeId>();
        if (depth >= 32 || points.Count >= MaximumPoints || !visited.Add(parent))
            return;
        session.Browse(
            null,
            null,
            parent,
            0u,
            BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences,
            true,
            (uint)(NodeClass.Object | NodeClass.Variable),
            out var continuationPoint,
            out var references);
        while (true)
        {
            foreach (var reference in references)
            {
                if (points.Count >= MaximumPoints)
                    break;
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (nodeId is null)
                    continue;
                var name = string.IsNullOrWhiteSpace(parentName)
                    ? reference.DisplayName.Text
                    : $"{parentName}/{reference.DisplayName.Text}";
                if (reference.NodeClass == NodeClass.Variable)
                {
                    var path = nodeId.ToString();
                    points.Add(new AcquisitionProbePoint
                    {
                        Path = path,
                        Name = name,
                        Kind = "opc-variable",
                        DataType = "unknown"
                    });
                }
                else
                {
                    BrowseOpcNodes(session, nodeId, name, depth + 1, values, points, visited);
                }
            }
            if (points.Count >= MaximumPoints || continuationPoint is null || continuationPoint.Length == 0)
                break;
            session.BrowseNext(
                null,
                false,
                continuationPoint,
                out continuationPoint,
                out references);
        }
    }
#pragma warning restore CS0618

    private static void FlattenJson(
        JsonElement element,
        string path,
        IDictionary<string, object?> values,
        ICollection<AcquisitionProbePoint> points,
        string? topic = null)
    {
        if (points.Count >= MaximumPoints)
            return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                FlattenJson(property.Value, Join(path, property.Name), values, points, topic);
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                FlattenJson(item, $"{path}[{index++}]", values, points, topic);
            return;
        }
        var raw = JsonValue(element);
        values[path] = raw;
        AddPoint(points, path, path, "json-field", raw, topic);
    }

    private static IReadOnlyList<AcquisitionMappingPreview> BuildPreviews(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> raw,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> topicValues)
    {
        var definitions = deployment.DataModel.Acquisition.DataItems
            .ToDictionary(item => item.Code, StringComparer.Ordinal);
        return deployment.Task.ValueMappings.Select(mapping =>
        {
            var previewRaw = raw;
            object? value;
            if (deployment.Task.Protocol == AcquisitionProtocols.Mqtt &&
                !string.IsNullOrWhiteSpace(mapping.Topic))
            {
                previewRaw = topicValues.TryGetValue(mapping.Topic, out var isolated)
                    ? isolated
                    : new Dictionary<string, object?>(StringComparer.Ordinal);
                previewRaw.TryGetValue(mapping.SourcePath, out value);
            }
            else
            {
                raw.TryGetValue(mapping.SourcePath, out value);
            }
            var sourceFound = value is not null;
            string? converted = null;
            string? error = null;
            try
            {
                var resolved = AcquisitionValuePolicy.Resolve(
                    previewRaw,
                    mapping,
                    definitions[mapping.DataItemCode].DataType);
                converted = Format(resolved);
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
            var acceptedWithoutSource = mapping.MissingValueBehavior == "use-default" ||
                                        (!mapping.Required && mapping.MissingValueBehavior is "inherit" or "omit");
            return new AcquisitionMappingPreview
            {
                DataItemCode = mapping.DataItemCode,
                SourcePath = mapping.SourcePath,
                Found = sourceFound,
                Accepted = error is null && (sourceFound || acceptedWithoutSource),
                RawValue = Format(value),
                ConvertedValue = converted,
                DataType = value?.GetType().Name,
                SourceUnit = mapping.SourceUnit,
                TargetUnit = definitions.GetValueOrDefault(mapping.DataItemCode)?.Unit,
                Error = error
            };
        }).ToArray();
    }

    private static ProbeSnapshot FromRegisterValues(
        Dictionary<string, object?> values,
        string kind,
        bool mappingsValidated = true)
        => new(
            values,
            values.Select(item => new AcquisitionProbePoint
            {
                Path = item.Key,
                Name = item.Key,
                Kind = kind,
                DataType = item.Value?.GetType().Name ?? "null",
                RawValue = Format(item.Value)
            }).ToArray(),
            mappingsValidated);

    private static void AddPoint(
        ICollection<AcquisitionProbePoint> points,
        string path,
        string name,
        string kind,
        object? value,
        string? topic = null)
    {
        if (string.IsNullOrWhiteSpace(path) || points.Count >= MaximumPoints)
            return;
        points.Add(new AcquisitionProbePoint
        {
            Path = path,
            Name = name,
            Kind = kind,
            DataType = value?.GetType().Name ?? "null",
            RawValue = Format(value),
            Topic = topic
        });
    }

    private static void AddOpcPoint(
        ICollection<AcquisitionProbePoint> points,
        string path,
        string name,
        DataValue value)
    {
        if (string.IsNullOrWhiteSpace(path) || points.Count >= MaximumPoints)
            return;
        points.Add(new AcquisitionProbePoint
        {
            Path = path,
            Name = name,
            Kind = "opc-variable",
            DataType = value.Value?.GetType().Name ?? "null",
            RawValue = Format(value.Value),
            Quality = value.StatusCode.ToString(),
            SourceTimestamp = value.SourceTimestamp == DateTime.MinValue
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc))
        });
    }

    private static DiscoveryPage ApplyDiscoveryQuery(
        IReadOnlyList<AcquisitionProbePoint> points,
        SourceDiscoveryQuery query)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var search = query.Search?.Trim();
        var root = query.RootPath?.Trim();
        var kinds = query.Kinds.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var namespaces = query.Namespaces.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim().TrimStart('n', 's', '='))
            .ToHashSet(StringComparer.Ordinal);
        var pathPattern = CompileDiscoveryPattern(query.PathPattern, "点位路径正则");
        var namePattern = CompileDiscoveryPattern(query.NamePattern, "点位名称正则");
        var cursor = DecodeCursor(query.Cursor);

        var filtered = points
            .Where(point => string.IsNullOrEmpty(search) ||
                            point.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            point.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            (point.Topic?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(point => string.IsNullOrEmpty(root) ||
                            point.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                            point.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Where(point => kinds.Count == 0 || kinds.Contains(point.Kind))
            .Where(point => namespaces.Count == 0 || namespaces.Contains(NodeNamespace(point.Path)))
            .Where(point => pathPattern is null || pathPattern.IsMatch(point.Path))
            .Where(point => namePattern is null || namePattern.IsMatch(point.Name))
            .OrderBy(static point => PointKey(point), StringComparer.Ordinal)
            .Where(point => cursor is null || string.CompareOrdinal(PointKey(point), cursor) > 0)
            .Take(pageSize + 1)
            .ToArray();
        var hasMore = filtered.Length > pageSize;
        var page = filtered.Take(pageSize).ToArray();
        return new DiscoveryPage(
            page,
            hasMore && page.Length > 0 ? EncodeCursor(PointKey(page[^1])) : null);
    }

    private static Regex? CompileDiscoveryPattern(string? pattern, string label)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            return new Regex(
                pattern.Trim(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"{label}无效：{exception.Message}", exception);
        }
    }

    private static string PointKey(AcquisitionProbePoint point) => $"{point.Topic}\u001f{point.Path}";

    private static string NodeNamespace(string path)
    {
        if (!path.StartsWith("ns=", StringComparison.Ordinal)) return "0";
        var separator = path.IndexOf(';');
        return separator > 3 ? path[3..separator] : string.Empty;
    }

    private static string EncodeCursor(string value)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var base64 = value.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("点位分页游标无效。", exception);
        }
    }

    private static IEnumerable<string> MappedPaths(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        var paths = task.ValueMappings.Select(item => item.SourcePath)
            .Concat(task.ValueMappings.Select(item => item.QualityPath))
            .Concat(task.ContextMappings.Select(item => item.SourcePath));
        if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
            paths = paths.Append(task.TimestampPath);
        if (!string.IsNullOrWhiteSpace(task.SequencePath)) paths = paths.Append(task.SequencePath);
        if (task.ProcessSpecification is { } specification)
            paths = paths.Append(specification.IdPath).Append(specification.VersionPath)
                .Append(specification.NamePath)
                .Concat(specification.ParameterMappings.Select(item => item.SourcePath))
                .Concat(specification.ParameterMappings.Select(item => item.QualityPath));
        return paths.Where(item => !string.IsNullOrWhiteSpace(item) &&
                                   item != "__probe_only__" && item != "$status")
            .Select(static item => item!)
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<PublicationEvidencePath> PublicationEvidencePaths(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        var paths = task.ValueMappings.SelectMany(static item => new[]
            {
                new PublicationEvidencePath(item.SourcePath, item.Topic),
                new PublicationEvidencePath(item.QualityPath, item.Topic)
            })
            .Concat(task.ContextMappings.Select(static item =>
                new PublicationEvidencePath(item.SourcePath, item.Topic)));
        if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
            paths = paths.Append(new PublicationEvidencePath(task.TimestampPath, null));
        if (!string.IsNullOrWhiteSpace(task.SequencePath))
            paths = paths.Append(new PublicationEvidencePath(task.SequencePath, null));
        if (task.ProcessSpecification is { } specification)
        {
            paths = paths.Append(new PublicationEvidencePath(specification.IdPath, null))
                .Append(new PublicationEvidencePath(specification.VersionPath, null))
                .Append(new PublicationEvidencePath(specification.NamePath, null))
                .Concat(specification.ParameterMappings.Select(item =>
                    new PublicationEvidencePath(
                        MqttSnapshotAssembler.Combine(specification.ParametersPath, item.SourcePath),
                        item.Topic)))
                .Concat(specification.ParameterMappings
                    .Where(static item => !string.IsNullOrWhiteSpace(item.QualityPath))
                    .Select(item => new PublicationEvidencePath(
                        MqttSnapshotAssembler.Combine(specification.ParametersPath, item.QualityPath!),
                        item.Topic)));
        }
        return paths.Where(static item => !string.IsNullOrWhiteSpace(item.Path) && item.Path != "$status")
            .Distinct();
    }

    private static bool EvidenceLocated(
        string protocol,
        PublicationEvidencePath evidence,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> topicValues)
    {
        if (protocol == AcquisitionProtocols.Mqtt && !string.IsNullOrWhiteSpace(evidence.Topic))
            return topicValues.TryGetValue(evidence.Topic, out var isolated) &&
                   isolated.TryGetValue(evidence.Path!, out var topicValue) && topicValue is not null;
        return values.TryGetValue(evidence.Path!, out var value) && value is not null;
    }

    private sealed record PublicationEvidencePath(string? Path, string? Topic)
    {
        public string Display => string.IsNullOrWhiteSpace(Topic) ? Path! : $"{Topic} → {Path}";
    }

    private static string Join(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}.{name}";

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static string? Format(object? value)
        => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    private sealed record ProbeSnapshot(
        Dictionary<string, object?> Values,
        IReadOnlyList<AcquisitionProbePoint> Points,
        bool MappingsValidated = true,
        DiscoveryPage? Page = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? TopicValuesSource = null)
    {
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> TopicValues { get; } =
            TopicValuesSource ?? new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
    }

    private sealed record DiscoveryPage(
        IReadOnlyList<AcquisitionProbePoint> Points,
        string? NextCursor);
}
