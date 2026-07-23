using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
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
        var connection = deployment.Profile.Mqtt
            ?? throw new InvalidOperationException("MQTT 连接配置不能为空。");
        var jsonOptions = JsonAcquisitionOptionsFactory.Create(deployment);
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        string? currentRecipe = null;
        var lifecycle = new AcquisitionLifecycleTracker();

        client.ApplicationMessageReceivedAsync += async message =>
        {
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            try
            {
                using var document = JsonDocument.Parse(message.ApplicationMessage.Payload);
                var mapped = HttpPollingSnapshotMapper.Map(
                    document.RootElement,
                    jsonOptions,
                    normalizedSource,
                    currentRecipe);
                foreach (var productionEvent in lifecycle.Track(
                             mapped,
                             deployment.Profile.Lifecycle,
                             deployment.DataModel.Acquisition.SamplePeriodMs))
                {
                    await sink.EmitAsync(productionEvent, ct).ConfigureAwait(false);
                }
                currentRecipe = mapped.RecipeIdentity;
                status.RecordSuccess(configurationKey, DateTimeOffset.UtcNow, currentRecipe);
            }
            catch (Exception exception)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "MQTT 采集任务 {Configuration} 无法处理主题 {Topic} 的消息",
                    configurationKey, message.ApplicationMessage.Topic);
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
                    logger.LogInformation("MQTT 采集任务已连接：Configuration={Configuration}, Broker={Host}:{Port}",
                        configurationKey, connection.Host, connection.Port);
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
