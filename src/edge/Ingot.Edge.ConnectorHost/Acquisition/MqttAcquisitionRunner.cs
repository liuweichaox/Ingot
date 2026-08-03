using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
///     MQTT 订阅采集器。
///
///     订阅多个主题时，每个点位可以绑定自己的来源主题，跨主题的值由
///     <see cref="MqttSnapshotAssembler"/> 合并成一份等价快照后再走统一的映射管线。
///     以前所有主题共用一套映射，等价于要求每条报文都是完整快照——界面允许配多个主题，
///     但多主题分别携带部分字段的场景实际上无法工作。
/// </summary>
public sealed class MqttAcquisitionRunner(
    IEventSink sink,
    IAcquisitionSecretResolver secrets,
    AcquisitionStatus status,
    ILogger<MqttAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.Mqtt;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Profile.Mqtt
            ?? throw new InvalidOperationException("MQTT 连接配置不能为空。");
        var jsonOptions = JsonAcquisitionOptionsFactory.Create(deployment);
        var assembler = new MqttSnapshotAssembler(
            MqttSnapshotAssembler.SlotsFor(deployment.Profile),
            connection.SnapshotMaxAgeSeconds);
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        string? currentRecipe = null;
        string? currentSnapshotFingerprint = null;
        string? lastIncompleteReason = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        // 消息回调可能并发进入；合并快照是共享状态，必须串行化。
        using var gate = new SemaphoreSlim(1, 1);

        client.ApplicationMessageReceivedAsync += async message =>
        {
            var topic = message.ApplicationMessage.Topic;
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var subscription = MqttSnapshotAssembler.SubscriptionFor(connection.Topics, topic);
                using var document = JsonDocument.Parse(message.ApplicationMessage.Payload);
                var payload = MqttSnapshotAssembler.Unwrap(document.RootElement, subscription?.PayloadRoot);
                var receivedAt = DateTimeOffset.UtcNow;
                var carriedValue = assembler.Ingest(topic, payload, receivedAt);
                if (!carriedValue)
                {
                    // 只携带上下文的主题更新状态但不触发采样，
                    // 否则采样率会被与工艺变量无关的主题放大。
                    logger.LogDebug(
                        "MQTT 采集任务 {Configuration} 收到主题 {Topic} 的上下文报文，已并入合并快照",
                        configurationKey, topic);
                    return;
                }

                if (!assembler.TryBuildSnapshot(
                        receivedAt,
                        out var snapshot,
                        out var missing,
                        out var staleValueCount))
                {
                    if (staleValueCount > 0)
                    {
                        status.RecordStaleSnapshotRejection(
                            configurationKey,
                            staleValueCount,
                            $"合并快照已拒绝：{missing}。");
                    }
                    if (!string.Equals(missing, lastIncompleteReason, StringComparison.Ordinal))
                    {
                        lastIncompleteReason = missing;
                        if (staleValueCount == 0)
                            status.RecordFailure(configurationKey, $"合并快照尚未完整：{missing}。");
                        logger.LogInformation(
                            "MQTT 采集任务 {Configuration} 等待其余主题：{Reason}", configurationKey, missing);
                    }

                    return;
                }

                lastIncompleteReason = null;
                using (snapshot)
                {
                    var topicSnapshots = assembler.BuildTopicSnapshots(receivedAt);
                    var fingerprint = MqttSnapshotAssembler.Fingerprint(snapshot!, topicSnapshots);
                    if (string.Equals(fingerprint, currentSnapshotFingerprint, StringComparison.Ordinal))
                    {
                        status.RecordSuccess(
                            configurationKey,
                            DateTimeOffset.UtcNow,
                            currentRecipe,
                            incrementSample: false);
                        return;
                    }

                    var mapped = HttpPollingSnapshotMapper.Map(
                        snapshot!.RootElement,
                        jsonOptions,
                        normalizedSource,
                        currentRecipe,
                        topicSnapshots);
                    // MQTT 由设备推送，没有固定采样周期，因此不向周期跟踪器提供轮询间隔。
                    var events = lifecycle.Track(mapped, deployment.Profile.Lifecycle, 0);
                    await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                    status.RecordCycleState(configurationKey, lifecycle.IsRunActive);
                    currentRecipe = mapped.RecipeIdentity;
                    currentSnapshotFingerprint = fingerprint;
                }

                status.RecordSuccess(configurationKey, DateTimeOffset.UtcNow, currentRecipe);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "MQTT 采集任务 {Configuration} 无法处理主题 {Topic} 的消息",
                    configurationKey, topic);
            }
            finally
            {
                gate.Release();
            }
        };

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(connection.Host, connection.Port)
            .WithClientId(string.IsNullOrWhiteSpace(connection.ClientId)
                ? $"ingot-{deployment.Profile.EdgeId}-{deployment.Profile.ProfileId}"
                : connection.ClientId)
            .WithProtocolVersion(connection.ProtocolVersion == "3.1.1"
                ? MqttProtocolVersion.V311
                : MqttProtocolVersion.V500)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(connection.KeepAliveSeconds))
            .WithCleanSession(connection.CleanSession);

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

        var options = optionsBuilder.Build();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                if (!client.IsConnected)
                {
                    await client.ConnectAsync(options, ct).ConfigureAwait(false);
                    foreach (var topic in connection.Topics)
                    {
                        var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                            .WithTopicFilter(filter => filter
                                .WithTopic(topic.Topic)
                                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)topic.Qos))
                            .Build();
                        await client.SubscribeAsync(subscribeOptions, ct).ConfigureAwait(false);
                    }
                    logger.LogInformation(
                        "MQTT 采集任务已连接：Configuration={Configuration}, Broker={Host}:{Port}, Topics={TopicCount}",
                        configurationKey, connection.Host, connection.Port, connection.Topics.Count);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "MQTT 采集任务 {Configuration} 连接失败，等待重连", configurationKey);
                await Task.Delay(deployment.Profile.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }
}
