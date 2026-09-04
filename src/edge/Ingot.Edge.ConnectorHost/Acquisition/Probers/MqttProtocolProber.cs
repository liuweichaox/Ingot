// MQTT 订阅协议探查。
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class MqttProtocolProber(
    IAcquisitionSecretResolver secrets,
    AcquisitionHttpEgressPolicy httpEgressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.Mqtt;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.Mqtt
            ?? throw new InvalidOperationException("MQTT 连接配置不能为空。");
        if (connection.Topics.Count == 0)
            throw new InvalidOperationException("MQTT 至少需要一个订阅主题。");

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        var discoveryProbe = AcquisitionProbeSupport.IsDiscoveryProbe(deployment);
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
                    AcquisitionProbeSupport.FlattenJson(discovered.Aggregate.RootElement, string.Empty, discoveredValues, []);
                    foreach (var topicSnapshot in discovered.TopicSnapshots)
                    {
                        var topicValues = new Dictionary<string, object?>(StringComparer.Ordinal);
                        AcquisitionProbeSupport.FlattenJson(
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
                    AcquisitionProbeSupport.FlattenJson(snapshot!.RootElement, string.Empty, values, []);
                    var topicSnapshots = assembler.BuildTopicSnapshots(receivedAt);
                    if (topicSnapshots.Count == 0)
                        AcquisitionProbeSupport.FlattenJson(snapshot.RootElement, string.Empty, new Dictionary<string, object?>(), points);
                    else
                        foreach (var topicSnapshot in topicSnapshots)
                        {
                            var isolated = new Dictionary<string, object?>(StringComparer.Ordinal);
                            AcquisitionProbeSupport.FlattenJson(
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
        var pinnedHost = await httpEgressPolicy.ResolvePinnedHostAsync(
            connection.Host,
            "MQTT",
            ct,
            AcquisitionProbeSupport.UsesCredentials(connection)).ConfigureAwait(false);
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(pinnedHost, connection.Port)
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
}
