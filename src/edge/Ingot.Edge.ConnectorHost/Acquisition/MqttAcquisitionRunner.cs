using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace Ingot.Edge.ConnectorHost.Acquisition;

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
        var connection = deployment.Task.Mqtt
            ?? throw new InvalidOperationException("MQTT 连接配置不能为空。");
        var jsonOptions = JsonAcquisitionOptionsFactory.Create(deployment);
        var assembler = new MqttSnapshotAssembler(
            MqttSnapshotAssembler.SlotsFor(deployment.Task),
            connection.SnapshotMaxAgeSeconds);
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        string? currentProcessSpecification = null;
        string? currentSnapshotFingerprint = null;
        string? lastIncompleteReason = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var lastMessageTicks = DateTimeOffset.UtcNow.UtcTicks;
        var subscriptionsReady = false;

        using var gate = new SemaphoreSlim(1, 1);

        client.ApplicationMessageReceivedAsync += async message =>
        {
            var topic = message.ApplicationMessage.Topic;
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var subscription = MqttSnapshotAssembler.SubscriptionFor(connection.Topics, topic);
                var decodedPayload = MqttPayloadDecoder.Decode(message.ApplicationMessage.Payload, connection);
                using var document = JsonDocument.Parse(decodedPayload, AcquisitionJsonLimits.DocumentOptions);
                var payload = MqttSnapshotAssembler.Unwrap(document.RootElement, subscription?.PayloadRoot);
                var receivedAt = DateTimeOffset.UtcNow;
                var carriedValue = assembler.Ingest(
                    topic,
                    payload,
                    receivedAt,
                    MqttTopicVariableResolver.Resolve(subscription, topic));
                if (!carriedValue)
                {

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
                    status.RecordReadSuccess(configurationKey, DateTimeOffset.UtcNow);
                    Interlocked.Exchange(ref lastMessageTicks, receivedAt.UtcTicks);
                    if (string.Equals(fingerprint, currentSnapshotFingerprint, StringComparison.Ordinal))
                    {
                        status.RecordDuplicateSnapshot(configurationKey, stalled: false);
                        return;
                    }

                    var mapped = HttpPollingSnapshotMapper.Map(
                        snapshot!.RootElement,
                        jsonOptions,
                        normalizedSource,
                        currentProcessSpecification,
                        topicSnapshots);

                    var events = lifecycle.Track(mapped, deployment.Task.Lifecycle, 0);
                    await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                    status.RecordProcessExecutionState(configurationKey, lifecycle.IsRunActive);
                    status.RecordEmissionOutcome(
                        configurationKey,
                        events.Count,
                        deployment.Task.Lifecycle is not null && events.Count == 0);
                    currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                    currentSnapshotFingerprint = fingerprint;
                    status.RecordValidSnapshot(
                        configurationKey,
                        DateTimeOffset.UtcNow,
                        currentProcessSpecification,
                        receivedAt);
                }
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
                ? $"ingot-{deployment.Task.EdgeId}-{deployment.Task.TaskId}"
                : connection.ClientId)
            .WithProtocolVersion(connection.ProtocolVersion == "3.1.1"
                ? MqttProtocolVersion.V311
                : MqttProtocolVersion.V500)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(connection.KeepAliveSeconds))
            .WithCleanSession(connection.ResetSessionOnConnect);

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

        var options = optionsBuilder.Build();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                if (!client.IsConnected)
                {
                    subscriptionsReady = false;
                    using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectTimeout.CancelAfter(deployment.Task.Execution.TimeoutMs);
                    try
                    {
                        await client.ConnectAsync(options, connectTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"连接 MQTT 消息服务器 {connection.Host}:{connection.Port} 超过 " +
                            $"{deployment.Task.Execution.TimeoutMs}ms 未完成。");
                    }
                    logger.LogInformation(
                        "MQTT 采集任务已连接：Configuration={Configuration}, Broker={Host}:{Port}",
                        configurationKey, connection.Host, connection.Port);
                }
                if (!subscriptionsReady)
                {
                    foreach (var topic in connection.Topics)
                    {
                        var subscribeOptions = factory.CreateSubscribeOptionsBuilder()
                            .WithTopicFilter(filter => filter
                                .WithTopic(topic.Topic)
                            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)topic.Qos))
                            .Build();
                        using var subscribeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        subscribeTimeout.CancelAfter(deployment.Task.Execution.TimeoutMs);
                        MqttClientSubscribeResult subscribeResult;
                        try
                        {
                            subscribeResult = await client.SubscribeAsync(subscribeOptions, subscribeTimeout.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            throw new TimeoutException(
                                $"订阅 MQTT 主题 {topic.Topic} 超过 " +
                                $"{deployment.Task.Execution.TimeoutMs}ms 未完成。");
                        }
                        MqttSubscriptionGuard.EnsureAccepted(subscribeResult, topic.Topic);
                    }
                    subscriptionsReady = true;
                    logger.LogInformation(
                        "MQTT 采集任务订阅就绪：Configuration={Configuration}, Topics={TopicCount}",
                        configurationKey, connection.Topics.Count);
                }
                await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                if (lifecycle.IsRunActive &&
                    deployment.Task.Execution.SourceIdentityStaleAfterMs > 0 &&
                    DateTimeOffset.UtcNow - new DateTimeOffset(
                        Interlocked.Read(ref lastMessageTicks), TimeSpan.Zero) >=
                    TimeSpan.FromMilliseconds(deployment.Task.Execution.SourceIdentityStaleAfterMs))
                    status.RecordFailure(
                        configurationKey,
                        $"活动过程执行期间 MQTT 数据源超过 {deployment.Task.Execution.SourceIdentityStaleAfterMs}ms 没有报文。");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                status.RecordFailure(configurationKey, "MQTT 设备操作超时。");
                logger.LogWarning("MQTT 采集任务 {Configuration} 操作超时，等待重连", configurationKey);
                await Task.Delay(deployment.Task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "MQTT 采集任务 {Configuration} 连接失败，等待重连", configurationKey);
                await Task.Delay(deployment.Task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }
}
