using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class OpcUaAcquisitionRunner(
    IEventSink sink,
    IAcquisitionSecretResolver secrets,
    AcquisitionStatus status,
    ILogger<OpcUaAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.OpcUa;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Profile.OpcUa
            ?? throw new InvalidOperationException("OPC UA 连接配置不能为空。");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                var configuration = await CreateConfigurationAsync(connection, secrets, ct).ConfigureAwait(false);
                var sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(_ => { }));
                using var discovery = await DiscoveryClient.CreateAsync(
                    configuration,
                    new Uri(connection.EndpointUrl),
                    DiagnosticsMasks.None,
                    ct).ConfigureAwait(false);
                var endpoints = await discovery.GetEndpointsAsync(null, ct).ConfigureAwait(false);
                var expectedMode = connection.SecurityMode switch
                {
                    "sign" => MessageSecurityMode.Sign,
                    "sign-and-encrypt" => MessageSecurityMode.SignAndEncrypt,
                    _ => MessageSecurityMode.None
                };
                var expectedPolicy = connection.SecurityPolicy switch
                {
                    "Basic256Sha256" => SecurityPolicies.Basic256Sha256,
                    "Aes128_Sha256_RsaOaep" => SecurityPolicies.Aes128_Sha256_RsaOaep,
                    "Aes256_Sha256_RsaPss" => SecurityPolicies.Aes256_Sha256_RsaPss,
                    _ => SecurityPolicies.None
                };
                var selectedEndpoint = endpoints
                    .Where(item => item.SecurityMode == expectedMode &&
                                   item.SecurityPolicyUri == expectedPolicy)
                    .OrderByDescending(item => item.SecurityLevel)
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"OPC UA 服务器不提供配置的安全组合：{connection.SecurityMode}/{connection.SecurityPolicy}。");
                var endpoint = new ConfiguredEndpoint(
                    null,
                    selectedEndpoint,
                    EndpointConfiguration.Create(configuration));
                var identity = CreateIdentity(connection, secrets);
                using var session = await sessionFactory.CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    $"Ingot {deployment.Profile.ProfileId}",
                    (uint)Math.Max(1000, deployment.Profile.Execution.TimeoutMs),
                    identity,
                    null,
                    ct).ConfigureAwait(false);

                var raw = new ConcurrentDictionary<string, object?>(StringComparer.Ordinal);
                var sourcePaths = SourcePaths(deployment).Distinct(StringComparer.Ordinal).ToArray();
                var required = RequiredSourcePaths(deployment)
                    .ToHashSet(StringComparer.Ordinal);
                var latestTimestampTicks = DateTimeOffset.UtcNow.UtcTicks;
                var currentRecipe = (string?)null;
                var lifecycle = new AcquisitionLifecycleTracker();
                var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = connection.PublishingIntervalMs,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    PublishingEnabled = true
                };
                foreach (var sourcePath in sourcePaths)
                {
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        DisplayName = sourcePath,
                        StartNodeId = NodeId.Parse(sourcePath),
                        AttributeId = Attributes.Value,
                        SamplingInterval = connection.SamplingIntervalMs,
                        QueueSize = 10,
                        DiscardOldest = true
                    };
                    item.Notification += (_, _) =>
                    {
                        foreach (var dataValue in item.DequeueValues())
                        {
                            if (StatusCode.IsBad(dataValue.StatusCode))
                            {
                                status.RecordFailure(configurationKey,
                                    $"OPC UA 节点 {sourcePath} 返回 {dataValue.StatusCode}。");
                                continue;
                            }
                            raw[sourcePath] = dataValue.Value;
                            var sourceTimestamp = dataValue.SourceTimestamp == DateTime.MinValue
                                ? DateTimeOffset.UtcNow
                                : new DateTimeOffset(
                                    DateTime.SpecifyKind(dataValue.SourceTimestamp, DateTimeKind.Utc));
                            Interlocked.Exchange(ref latestTimestampTicks, sourceTimestamp.UtcTicks);
                        }
                    };
                    subscription.AddItem(item);
                }
                session.AddSubscription(subscription);
                await subscription.CreateAsync(ct).ConfigureAwait(false);
                logger.LogInformation(
                    "OPC UA 采集任务已订阅：Configuration={Configuration}, Endpoint={Endpoint}, Nodes={NodeCount}",
                    configurationKey, connection.EndpointUrl, sourcePaths.Length);
                status.RecordSuccess(configurationKey, DateTimeOffset.UtcNow, null, incrementSample: false);

                while (!ct.IsCancellationRequested && session.Connected)
                {
                    await Task.Delay(connection.PublishingIntervalMs, ct).ConfigureAwait(false);
                    if (required.Any(path => !raw.ContainsKey(path)))
                        continue;
                    var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                        deployment,
                        new Dictionary<string, object?>(raw, StringComparer.Ordinal),
                        normalizedSource,
                        currentRecipe,
                        new DateTimeOffset(Interlocked.Read(ref latestTimestampTicks), TimeSpan.Zero));
                    foreach (var productionEvent in lifecycle.Track(
                                 mapped,
                                 deployment.Profile.Lifecycle,
                                 deployment.DataModel.Acquisition.SamplePeriodMs))
                    {
                        await sink.EmitAsync(productionEvent, ct).ConfigureAwait(false);
                    }
                    currentRecipe = mapped.RecipeIdentity;
                    status.RecordSuccess(configurationKey, DateTimeOffset.UtcNow, null);
                }
                if (!ct.IsCancellationRequested)
                    throw new IOException("OPC UA 会话已断开。");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "OPC UA 采集任务 {Configuration} 连接失败，等待重连", configurationKey);
                await Task.Delay(deployment.Profile.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<string> SourcePaths(AcquisitionDeployment deployment)
    {
        foreach (var mapping in deployment.Profile.ValueMappings)
            yield return mapping.SourcePath;
        foreach (var mapping in deployment.Profile.ContextMappings)
            yield return mapping.SourcePath;
        if (deployment.Profile.Recipe is not { } recipe)
            yield break;
        yield return recipe.IdPath;
        yield return recipe.VersionPath;
        if (!string.IsNullOrWhiteSpace(recipe.NamePath))
            yield return recipe.NamePath;
        foreach (var mapping in recipe.ParameterMappings)
            yield return mapping.SourcePath;
    }

    private static IEnumerable<string> RequiredSourcePaths(AcquisitionDeployment deployment)
    {
        foreach (var mapping in deployment.Profile.ValueMappings.Where(item => item.Required))
            yield return mapping.SourcePath;
        foreach (var mapping in deployment.Profile.ContextMappings.Where(item => item.Required))
            yield return mapping.SourcePath;
        if (deployment.Profile.Recipe is not { } recipe)
            yield break;
        yield return recipe.IdPath;
        yield return recipe.VersionPath;
        foreach (var mapping in recipe.ParameterMappings.Where(item => item.Required))
            yield return mapping.SourcePath;
    }

    private static IUserIdentity CreateIdentity(
        OpcUaConnection connection,
        IAcquisitionSecretResolver secrets)
        => connection.AuthenticationType switch
        {
            "username" => new UserIdentity(
                connection.Username ?? throw new InvalidOperationException("OPC UA 用户名不能为空。"),
                Encoding.UTF8.GetBytes(secrets.Resolve(connection.PasswordSecretRef) ?? string.Empty)),
            "certificate" => new UserIdentity(LoadCertificate(
                connection.ClientCertificatePath,
                secrets.Resolve(connection.ClientCertificatePasswordSecretRef))),
            _ => new UserIdentity(new AnonymousIdentityToken())
        };

    private static X509Certificate2 LoadCertificate(string? path, string? password)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("OPC UA 证书认证需要配置客户端证书路径。");
        return X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }

    private static async Task<ApplicationConfiguration> CreateConfigurationAsync(
        OpcUaConnection connection,
        IAcquisitionSecretResolver secrets,
        CancellationToken ct)
    {
        var applicationCertificate = string.IsNullOrWhiteSpace(connection.ClientCertificatePath)
            ? new CertificateIdentifier()
            : new CertificateIdentifier(LoadCertificate(
                connection.ClientCertificatePath,
                secrets.Resolve(connection.ClientCertificatePasswordSecretRef)));
        var certificateStoreRoot = Path.Combine(Path.GetTempPath(), "ingot-edge-opcua");
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "Ingot Edge OPC UA Client",
            ApplicationUri = $"urn:{Utils.GetHostName()}:Ingot:Edge:OpcUa",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = applicationCertificate,
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(certificateStoreRoot, "issuers")
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(certificateStoreRoot, "trusted")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(certificateStoreRoot, "rejected")
                },
                AutoAcceptUntrustedCertificates = connection.TrustServerCertificate,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 10000
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60000
            }
        };
        await configuration.ValidateAsync(ApplicationType.Client, ct).ConfigureAwait(false);
        configuration.CertificateValidator.CertificateValidation += (_, args) =>
        {
            if (connection.TrustServerCertificate)
                args.Accept = true;
        };
        return configuration;
    }
}
