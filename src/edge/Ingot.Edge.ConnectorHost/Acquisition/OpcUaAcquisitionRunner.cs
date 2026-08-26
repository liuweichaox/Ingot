// 通过固定端点和受控凭据订阅 OPC UA 点位并发布生产事件。
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
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<OpcUaAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.OpcUa;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Task.OpcUa
            ?? throw new InvalidOperationException("OPC UA 连接配置不能为空。");
        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectTimeout.CancelAfter(deployment.Task.Execution.TimeoutMs);
                var connectCt = connectTimeout.Token;
                var usesCredentials = UsesCredentials(connection);
                var discoveryUri = await egressPolicy.ResolvePinnedEndpointAsync(
                    new Uri(connection.EndpointUrl),
                    "OPC UA",
                    connectCt,
                    usesCredentials).ConfigureAwait(false);
                var configuration = await CreateConfigurationAsync(
                    connection, secrets, connectCt, deployment.Task.Execution.TimeoutMs).ConfigureAwait(false);
                var sessionFactory = new DefaultSessionFactory(DefaultTelemetry.Create(_ => { }));
                using var discovery = await DiscoveryClient.CreateAsync(
                    configuration,
                    discoveryUri,
                    DiagnosticsMasks.None,
                    connectCt).ConfigureAwait(false);
                var endpoints = await discovery.GetEndpointsAsync(null, connectCt).ConfigureAwait(false);
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
                selectedEndpoint.EndpointUrl = (await egressPolicy.ResolvePinnedEndpointAsync(
                    new Uri(selectedEndpoint.EndpointUrl),
                    "OPC UA",
                    connectCt,
                    usesCredentials).ConfigureAwait(false)).ToString();
                var endpoint = new ConfiguredEndpoint(
                    null,
                    selectedEndpoint,
                    EndpointConfiguration.Create(configuration));
                var identity = CreateIdentity(connection, secrets);
                using var session = await sessionFactory.CreateAsync(
                    configuration,
                    endpoint,
                    false,
                    $"Ingot {deployment.Task.TaskId}",
                    (uint)Math.Max(1000, deployment.Task.Execution.TimeoutMs),
                    identity,
                    null,
                    connectCt).ConfigureAwait(false);

                var raw = new ConcurrentDictionary<string, object?>(StringComparer.Ordinal);
                var valueTimestamps = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                var sourcePaths = SourcePaths(deployment).Distinct(StringComparer.Ordinal).ToArray();
                var acceptedQuality = deployment.Task.ValueMappings
                    .Concat(deployment.Task.ProcessSpecification?.ParameterMappings ?? [])
                    .Where(static item => !string.IsNullOrWhiteSpace(item.QualityPath))
                    .GroupBy(static item => item.SourcePath, StringComparer.Ordinal)
                    .ToDictionary(
                        static group => group.Key,
                        static group => group.SelectMany(static item => item.AcceptedQualityValues)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase),
                        StringComparer.Ordinal);
                var required = RequiredSourcePaths(deployment)
                    .ToHashSet(StringComparer.Ordinal);
                var latestTimestampTicks = DateTimeOffset.UtcNow.UtcTicks;
                var latestNotificationTicks = DateTimeOffset.UtcNow.UtcTicks;
                long notificationVersion = 0;
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
                            var statusText = dataValue.StatusCode.ToString();
                            var explicitlyAccepted = acceptedQuality.TryGetValue(sourcePath, out var allowed) &&
                                                     allowed.Contains(statusText);
                            if (!StatusCode.IsGood(dataValue.StatusCode) && !explicitlyAccepted)
                            {
                                status.RecordFailure(configurationKey,
                                    $"OPC UA 节点 {sourcePath} 返回 {dataValue.StatusCode}。");
                                continue;
                            }
                            var receivedAt = DateTimeOffset.UtcNow;
                            var sourceTimestamp = dataValue.SourceTimestamp == DateTime.MinValue
                                ? receivedAt
                                : new DateTimeOffset(
                                    DateTime.SpecifyKind(dataValue.SourceTimestamp, DateTimeKind.Utc));
                            if (deployment.Task.Execution.MaximumFutureTimestampSkewMs > 0 &&
                                sourceTimestamp > receivedAt.AddMilliseconds(
                                    deployment.Task.Execution.MaximumFutureTimestampSkewMs))
                            {
                                status.RecordFailure(
                                    configurationKey,
                                    $"OPC UA 节点 {sourcePath} 的源时间戳超前 Edge 接收时间超过 " +
                                    $"{deployment.Task.Execution.MaximumFutureTimestampSkewMs}ms。");
                                continue;
                            }
                            raw[sourcePath] = dataValue.Value;
                            raw[$"$status:{sourcePath}"] = dataValue.StatusCode.ToString();
                            Interlocked.Increment(ref notificationVersion);
                            Interlocked.Exchange(ref latestNotificationTicks, receivedAt.UtcTicks);
                            valueTimestamps[sourcePath] = sourceTimestamp;
                            Interlocked.Exchange(ref latestTimestampTicks, sourceTimestamp.UtcTicks);
                        }
                    };
                    subscription.AddItem(item);
                }
                session.AddSubscription(subscription);
                await subscription.CreateAsync(connectCt).ConfigureAwait(false);
                var rejectedItems = subscription.MonitoredItems
                    .Where(item => ServiceResult.IsBad(item.Status.Error))
                    .Select(item => $"{item.DisplayName}: {item.Status.Error}")
                    .ToArray();
                if (rejectedItems.Length > 0)
                    throw new InvalidDataException(
                        $"OPC UA 服务器拒绝了 {rejectedItems.Length} 个监控点位：{string.Join("；", rejectedItems)}");
                logger.LogInformation(
                    "OPC UA 采集任务已订阅：Configuration={Configuration}, Endpoint={Endpoint}, Nodes={NodeCount}",
                    configurationKey, connection.EndpointUrl, sourcePaths.Length);

                var emittedNotificationVersion = Interlocked.Read(ref notificationVersion);
                var firstSnapshotDeadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(
                    deployment.Task.Execution.TimeoutMs,
                    connection.PublishingIntervalMs * 3));
                var receivedValidSnapshot = false;
                while (!ct.IsCancellationRequested && session.Connected)
                {
                    await Task.Delay(connection.PublishingIntervalMs, ct).ConfigureAwait(false);
                    if (!receivedValidSnapshot && DateTimeOffset.UtcNow >= firstSnapshotDeadline)
                    {
                        var unavailable = required.Where(path => !raw.ContainsKey(path)).ToArray();
                        throw new TimeoutException(unavailable.Length == 0
                            ? "OPC UA 订阅已建立，但启动期限内没有形成有效快照。"
                            : $"OPC UA 启动期限内未收到必需点位：{string.Join("、", unavailable)}。");
                    }
                    var currentNotificationVersion = Interlocked.Read(ref notificationVersion);
                    if (currentNotificationVersion == emittedNotificationVersion)
                    {
                        if (lifecycle.IsRunActive &&
                            deployment.Task.Execution.SourceIdentityStaleAfterMs > 0 &&
                            DateTimeOffset.UtcNow - new DateTimeOffset(
                                Interlocked.Read(ref latestNotificationTicks), TimeSpan.Zero) >=
                            TimeSpan.FromMilliseconds(deployment.Task.Execution.SourceIdentityStaleAfterMs))
                            status.RecordFailure(
                                configurationKey,
                                $"活动过程执行期间 OPC UA 点位超过 {deployment.Task.Execution.SourceIdentityStaleAfterMs}ms 没有更新。");
                        continue;
                    }
                    if (required.Any(path => !raw.ContainsKey(path)))
                        continue;
                    var observedAt = DateTimeOffset.UtcNow;
                    var requiredTimestamps = required.Select(path => valueTimestamps[path]).ToArray();
                    var stale = requiredTimestamps.Count(timestamp =>
                        observedAt - timestamp > TimeSpan.FromMilliseconds(connection.MaximumValueAgeMs));
                    if (stale > 0)
                    {
                        status.RecordStaleSnapshotRejection(
                            configurationKey,
                            stale,
                            $"OPC UA 快照中有 {stale} 个必需点位超过 {connection.MaximumValueAgeMs}ms 未更新。");
                        emittedNotificationVersion = currentNotificationVersion;
                        continue;
                    }
                    if (requiredTimestamps.Length > 1 &&
                        requiredTimestamps.Max() - requiredTimestamps.Min() >
                        TimeSpan.FromMilliseconds(connection.MaximumTimestampSkewMs))
                    {
                        status.RecordStaleSnapshotRejection(
                            configurationKey,
                            requiredTimestamps.Length,
                            $"OPC UA 必需点位源时间跨度超过 {connection.MaximumTimestampSkewMs}ms。快照未输出。");
                        emittedNotificationVersion = currentNotificationVersion;
                        continue;
                    }
                    status.RecordReadSuccess(configurationKey, observedAt);
                    var snapshotValues = new Dictionary<string, object?>(raw, StringComparer.Ordinal);
                    foreach (var sourcePath in sourcePaths)
                    {
                        if (required.Contains(sourcePath) ||
                            !valueTimestamps.TryGetValue(sourcePath, out var timestamp) ||
                            observedAt - timestamp <= TimeSpan.FromMilliseconds(connection.MaximumValueAgeMs))
                            continue;
                        snapshotValues.Remove(sourcePath);
                        snapshotValues.Remove($"$status:{sourcePath}");
                    }
                    var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                        deployment,
                        snapshotValues,
                        normalizedSource,
                        currentProcessSpecification,
                        new DateTimeOffset(Interlocked.Read(ref latestTimestampTicks), TimeSpan.Zero));
                    var deduplication = sourceDeduplicator.Evaluate(
                        mapped.Sample,
                        observedAt,
                        TimeSpan.FromMilliseconds(deployment.Task.Execution.SourceIdentityStaleAfterMs));
                    if (deduplication is AcquisitionDeduplicationResult.Duplicate or AcquisitionDeduplicationResult.Stalled)
                    {
                        currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                        emittedNotificationVersion = currentNotificationVersion;
                        status.RecordDuplicateSnapshot(
                            configurationKey,
                            deduplication == AcquisitionDeduplicationResult.Stalled,
                            $"设备源身份超过 {deployment.Task.Execution.SourceIdentityStaleAfterMs}ms 未变化。");
                        continue;
                    }
                    var events = lifecycle.Track(
                        mapped,
                        deployment.Task.Lifecycle,
                        connection.PublishingIntervalMs);
                    await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                    status.RecordProcessExecutionState(configurationKey, lifecycle.IsRunActive);
                    status.RecordEmissionOutcome(
                        configurationKey,
                        events.Count,
                        deployment.Task.Lifecycle is not null && events.Count == 0);
                    currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                    emittedNotificationVersion = currentNotificationVersion;
                    status.RecordValidSnapshot(
                        configurationKey,
                        observedAt,
                        currentProcessSpecification,
                        deduplication == AcquisitionDeduplicationResult.Changed ? observedAt : null);
                    receivedValidSnapshot = true;
                }
                if (!ct.IsCancellationRequested)
                    throw new IOException("OPC UA 会话已断开。");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var error = $"OPC UA 连接或订阅建立超过 {deployment.Task.Execution.TimeoutMs}ms 未完成。";
                status.RecordFailure(configurationKey, error);
                logger.LogWarning("OPC UA 采集任务 {Configuration} 操作超时，等待重连", configurationKey);
                await Task.Delay(deployment.Task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "OPC UA 采集任务 {Configuration} 连接失败，等待重连", configurationKey);
                await Task.Delay(deployment.Task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<string> SourcePaths(AcquisitionDeployment deployment)
    {
        foreach (var mapping in deployment.Task.ValueMappings)
            yield return mapping.SourcePath;
        foreach (var mapping in deployment.Task.ContextMappings)
            yield return mapping.SourcePath;
        if (deployment.Task.ProcessSpecification is not { } processSpecification)
            yield break;
        yield return processSpecification.IdPath;
        yield return processSpecification.VersionPath;
        if (!string.IsNullOrWhiteSpace(processSpecification.NamePath))
            yield return processSpecification.NamePath;
        foreach (var mapping in processSpecification.ParameterMappings)
            yield return mapping.SourcePath;
    }

    private static IEnumerable<string> RequiredSourcePaths(AcquisitionDeployment deployment)
    {
        foreach (var mapping in deployment.Task.ValueMappings.Where(item => item.Required))
            yield return mapping.SourcePath;
        foreach (var mapping in deployment.Task.ContextMappings.Where(item => item.Required))
            yield return mapping.SourcePath;
        if (deployment.Task.ProcessSpecification is not { } processSpecification)
            yield break;
        yield return processSpecification.IdPath;
        yield return processSpecification.VersionPath;
        foreach (var mapping in processSpecification.ParameterMappings.Where(item => item.Required))
            yield return mapping.SourcePath;
    }

    private static bool UsesCredentials(OpcUaConnection connection)
        => connection.AuthenticationType != "anonymous" ||
           !string.IsNullOrWhiteSpace(connection.Username) ||
           !string.IsNullOrWhiteSpace(connection.PasswordSecretRef) ||
           !string.IsNullOrWhiteSpace(connection.ClientCertificatePath) ||
           !string.IsNullOrWhiteSpace(connection.ClientCertificatePasswordSecretRef);

    internal static IUserIdentity CreateIdentity(
        OpcUaConnection connection,
        IAcquisitionSecretResolver secrets)
        => connection.AuthenticationType switch
        {
            "username" => new UserIdentity(
                connection.Username ?? throw new InvalidOperationException("OPC UA 用户名不能为空。"),
                Encoding.UTF8.GetBytes(AcquisitionSecretReference.ResolveRequired(
                    secrets, connection.PasswordSecretRef, "OPC UA 密码"))),
            "certificate" => new UserIdentity(LoadCertificate(
                connection.ClientCertificatePath,
                AcquisitionSecretReference.ResolveOptional(
                    secrets, connection.ClientCertificatePasswordSecretRef, "OPC UA 客户端证书密码"))),
            _ => new UserIdentity(new AnonymousIdentityToken())
        };

    private static X509Certificate2 LoadCertificate(string? path, string? password)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("OPC UA 证书认证需要配置客户端证书路径。");
        return X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }

    internal static async Task<ApplicationConfiguration> CreateConfigurationAsync(
        OpcUaConnection connection,
        IAcquisitionSecretResolver secrets,
        CancellationToken ct,
        int operationTimeoutMs = 10_000)
    {
        var applicationCertificate = string.IsNullOrWhiteSpace(connection.ClientCertificatePath)
            ? new CertificateIdentifier()
            : new CertificateIdentifier(LoadCertificate(
                connection.ClientCertificatePath,
                AcquisitionSecretReference.ResolveOptional(
                    secrets, connection.ClientCertificatePasswordSecretRef, "OPC UA 客户端证书密码")));
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
                OperationTimeout = operationTimeoutMs
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
