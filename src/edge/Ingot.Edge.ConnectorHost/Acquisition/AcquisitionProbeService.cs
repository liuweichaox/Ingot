using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
    private const int MaximumPoints = 500;

    public async Task<AcquisitionProbeResult> ProbeAsync(
        AcquisitionDeployment deployment,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
            deployment.Profile.Execution.TimeoutMs,
            500,
            30_000));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var ct = timeoutSource.Token;

        var raw = deployment.Profile.Protocol switch
        {
            AcquisitionProtocols.HttpPolling => await ProbeHttpAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.Mqtt => await ProbeMqttAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.OpcUa => await ProbeOpcUaAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.ModbusTcp => await ProbeModbusAsync(deployment, ct).ConfigureAwait(false),
            AcquisitionProtocols.MelsecA1E => await ProbeMelsecAsync(deployment, ct).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"不支持采集协议 {deployment.Profile.Protocol}。")
        };

        var previews = BuildPreviews(deployment, raw.Values);
        var missing = previews.Where(item => !item.Found && Required(deployment, item.DataItemCode)).ToArray();
        return new AcquisitionProbeResult
        {
            Success = missing.Length == 0 && raw.MappingsValidated,
            MappingsValidated = missing.Length == 0 && raw.MappingsValidated,
            Protocol = deployment.Profile.Protocol,
            TestedAt = DateTimeOffset.UtcNow,
            Message = missing.Length == 0 && raw.MappingsValidated
                ? $"连接成功，读取到 {raw.Points.Count} 个设备点位，映射验证通过。"
                : missing.Length > 0
                    ? $"连接成功，但有 {missing.Length} 个必需映射未读取到值。"
                    : "连接成功，但设备报文未通过映射验证。",
            Points = raw.Points,
            Mappings = previews
        };
    }

    private async Task<ProbeSnapshot> ProbeHttpAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Profile.Connection;
        var requestUri = new Uri(new Uri(connection.BaseUrl), connection.SnapshotPath);
        using var response = await httpClientFactory.CreateClient()
            .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 64 },
            ct).ConfigureAwait(false);
        var mappingsValidated = true;
        if (!IsDiscoveryProbe(deployment))
        {
            try
            {
                HttpPollingSnapshotMapper.Map(
                    document.RootElement,
                    JsonAcquisitionOptionsFactory.Create(deployment),
                    deployment.Profile.Source,
                    previousRecipeIdentity: null);
            }
            catch (InvalidDataException)
            {
                mappingsValidated = false;
            }
        }
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var points = new List<AcquisitionProbePoint>();
        FlattenJson(document.RootElement, string.Empty, values, points);
        return new ProbeSnapshot(values, points, mappingsValidated);
    }

    private async Task<ProbeSnapshot> ProbeMqttAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Profile.Mqtt
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
                MqttSnapshotAssembler.SlotsFor(deployment.Profile),
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
                if (discoverySnapshots is not null)
                {
                    using var discovered = discoverySnapshots.Add(
                        topic,
                        message.ApplicationMessage.Payload,
                        receivedAt,
                        connection.SnapshotMaxAgeSeconds,
                        connection.SnapshotMaxAgeSeconds);
                    if (!discovered.IsComplete || !discovered.IsCoherent)
                        return;
                    var discoveredValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var discoveredPoints = new List<AcquisitionProbePoint>();
                    FlattenJson(discovered.Aggregate.RootElement, string.Empty, discoveredValues, discoveredPoints);
                    sample.TrySetResult(new ProbeSnapshot(discoveredValues, discoveredPoints));
                    return;
                }

                var subscription = MqttSnapshotAssembler.SubscriptionFor(connection.Topics, topic);
                using var document = JsonDocument.Parse(message.ApplicationMessage.Payload);
                var payload = MqttSnapshotAssembler.Unwrap(document.RootElement, subscription?.PayloadRoot);
                assembler!.Ingest(topic, payload, receivedAt);
                if (!assembler.TryBuildSnapshot(receivedAt, out var snapshot, out _))
                    return;
                using (snapshot)
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                    var points = new List<AcquisitionProbePoint>();
                    FlattenJson(snapshot!.RootElement, string.Empty, values, points);
                    var mappingsValidated = true;
                    try
                    {
                        HttpPollingSnapshotMapper.Map(
                            snapshot.RootElement,
                            jsonOptions,
                            deployment.Profile.Source,
                            previousRecipeIdentity: null,
                            assembler.BuildTopicSnapshots(receivedAt));
                    }
                    catch (InvalidDataException)
                    {
                        mappingsValidated = false;
                    }

                    sample.TrySetResult(new ProbeSnapshot(values, points, mappingsValidated));
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
        if (!string.IsNullOrWhiteSpace(connection.Username))
            optionsBuilder.WithCredentials(connection.Username, secrets.Resolve(connection.PasswordSecretRef));
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
                        secrets.Resolve(connection.ClientCertificatePasswordSecretRef));
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
            await client.SubscribeAsync(subscription, ct).ConfigureAwait(false);
        }
        var result = await sample.Task.WaitAsync(ct).ConfigureAwait(false);
        await client.DisconnectAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private async Task<ProbeSnapshot> ProbeModbusAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Profile.ModbusTcp
            ?? throw new InvalidOperationException("Modbus TCP 连接配置不能为空。");
        using var client = new TcpClient();
        await client.ConnectAsync(connection.Host, connection.Port, ct).ConfigureAwait(false);
        var factory = new ModbusFactory();
        using var master = factory.CreateMaster(client);
        var values = await ModbusTcpAcquisitionRunner.ReadSnapshotAsync(
            master,
            connection.UnitId,
            ModbusTcpAcquisitionRunner.BuildSelectors(deployment, connection.AddressBase)).ConfigureAwait(false);
        var mappingsValidated = ValidateProtocolMapping(deployment, values);
        return FromRegisterValues(values, "register", mappingsValidated);
    }

    private static async Task<ProbeSnapshot> ProbeMelsecAsync(
        AcquisitionDeployment deployment,
        CancellationToken ct)
    {
        var connection = deployment.Profile.MelsecA1E
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

    private async Task<ProbeSnapshot> ProbeOpcUaAsync(AcquisitionDeployment deployment, CancellationToken ct)
    {
        var connection = deployment.Profile.OpcUa
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
            $"Ingot probe {deployment.Profile.ProfileId}",
            (uint)Math.Clamp(deployment.Profile.Execution.TimeoutMs, 1000, 30_000),
            OpcUaAcquisitionRunner.CreateIdentity(connection, secrets),
            null,
            ct).ConfigureAwait(false);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var points = new List<AcquisitionProbePoint>();
        BrowseOpcNodes(session, ObjectIds.ObjectsFolder, string.Empty, 0, values, points);
#pragma warning disable CS0618 // OPC Foundation keeps the synchronous compatibility API; probe work is already isolated on an async request.
        foreach (var path in MappedPaths(deployment).Where(path => !values.ContainsKey(path)))
        {
            var value = session.ReadValue(NodeId.Parse(path));
            if (StatusCode.IsGood(value.StatusCode))
            {
                values[path] = value.Value;
                AddPoint(points, path, path, "opc-variable", value.Value);
            }
        }
#pragma warning restore CS0618
        var mappingsValidated = ValidateProtocolMapping(deployment, values);
        return new ProbeSnapshot(values, points, mappingsValidated);
    }

    private static bool IsDiscoveryProbe(AcquisitionDeployment deployment)
        => deployment.Profile.ValueMappings.Any(item =>
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
            if (deployment.Profile.TimestampMode == "source" &&
                !string.IsNullOrWhiteSpace(deployment.Profile.TimestampPath))
            {
                if (!values.TryGetValue(deployment.Profile.TimestampPath, out var rawTimestamp) ||
                    rawTimestamp is null)
                {
                    throw new InvalidDataException(
                        $"配置的时间来源没有读到值：{deployment.Profile.TimestampPath}。");
                }
                occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(
                    Convert.ToInt64(rawTimestamp, CultureInfo.InvariantCulture));
            }

            ProtocolAcquisitionSnapshotMapper.Map(
                deployment,
                values,
                deployment.Profile.Source,
                previousRecipeIdentity: null,
                occurredAt: occurredAt);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

#pragma warning disable CS0618 // See ProbeOpcUaAsync: the bounded compatibility browse API is used for one-shot discovery.
    private static void BrowseOpcNodes(
        Opc.Ua.Client.ISession session,
        NodeId parent,
        string parentName,
        int depth,
        IDictionary<string, object?> values,
        ICollection<AcquisitionProbePoint> points)
    {
        if (depth >= 4 || points.Count >= MaximumPoints)
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
            out _,
            out var references);
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
                var value = session.ReadValue(nodeId);
                var path = nodeId.ToString();
                values[path] = StatusCode.IsGood(value.StatusCode) ? value.Value : null;
                AddPoint(points, path, name, "opc-variable", values[path]);
            }
            else
            {
                BrowseOpcNodes(session, nodeId, name, depth + 1, values, points);
            }
        }
    }
#pragma warning restore CS0618

    private static void FlattenJson(
        JsonElement element,
        string path,
        IDictionary<string, object?> values,
        ICollection<AcquisitionProbePoint> points)
    {
        if (points.Count >= MaximumPoints)
            return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                FlattenJson(property.Value, Join(path, property.Name), values, points);
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                FlattenJson(item, $"{path}[{index++}]", values, points);
            return;
        }
        var raw = JsonValue(element);
        values[path] = raw;
        AddPoint(points, path, path, "json-field", raw);
    }

    private static IReadOnlyList<AcquisitionMappingPreview> BuildPreviews(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> raw)
    {
        var units = deployment.DataModel.Acquisition.DataItems
            .ToDictionary(item => item.Code, item => item.Unit, StringComparer.Ordinal);
        return deployment.Profile.ValueMappings.Select(mapping =>
        {
            var found = raw.TryGetValue(mapping.SourcePath, out var value);
            string? converted = null;
            string? error = null;
            if (found)
            {
                try
                {
                    converted = Transform(value, mapping);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
            }
            return new AcquisitionMappingPreview
            {
                DataItemCode = mapping.DataItemCode,
                SourcePath = mapping.SourcePath,
                Found = found && error is null,
                RawValue = Format(value),
                ConvertedValue = converted,
                DataType = value?.GetType().Name,
                Unit = units.GetValueOrDefault(mapping.DataItemCode),
                Error = error
            };
        }).ToArray();
    }

    private static string Transform(object? value, AcquisitionValueMapping mapping)
    {
        if (value is null)
            return string.Empty;
        if (value is string && mapping.Scale == 1 && mapping.Offset == 0)
            return Format(value) ?? string.Empty;
        if (value is bool boolean && mapping.Scale == 1 && mapping.Offset == 0)
            return boolean ? "true" : "false";
        var numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return (numeric * mapping.Scale + mapping.Offset).ToString("G15", CultureInfo.InvariantCulture);
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
        object? value)
    {
        if (string.IsNullOrWhiteSpace(path) || points.Count >= MaximumPoints)
            return;
        points.Add(new AcquisitionProbePoint
        {
            Path = path,
            Name = name,
            Kind = kind,
            DataType = value?.GetType().Name ?? "null",
            RawValue = Format(value)
        });
    }

    private static bool Required(AcquisitionDeployment deployment, string dataItemCode)
        => deployment.Profile.ValueMappings.FirstOrDefault(item =>
            item.DataItemCode == dataItemCode)?.Required == true;

    private static IEnumerable<string> MappedPaths(AcquisitionDeployment deployment)
        => deployment.Profile.ValueMappings.Select(item => item.SourcePath)
            .Concat(deployment.Profile.ContextMappings.Select(item => item.SourcePath))
            .Where(item => !string.IsNullOrWhiteSpace(item) && item != "__probe_only__")
            .Distinct(StringComparer.Ordinal);

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
        bool MappingsValidated = true);
}
