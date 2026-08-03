using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
///     执行平台发布给当前边缘节点的采集配置。配置版本不可变，发布新版本时平滑替换对应工作器。
/// </summary>
internal sealed class HttpPollingAcquisitionHostedService(
    IHttpClientFactory httpClientFactory,
    IEventSink sink,
    IEdgeIdentityProvider identity,
    IOptions<HttpPollingAcquisitionOptions> configuredOptions,
    IOptions<EdgeReportingOptions> edgeOptions,
    IAcquisitionDeploymentCache deploymentCache,
    AcquisitionProbeService probeService,
    IEnumerable<IAcquisitionProtocolRunner> protocolRunners,
    AcquisitionStatus status,
    ILogger<HttpPollingAcquisitionHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpPollingAcquisitionOptions _localOptions = configuredOptions.Value;
    private readonly EdgeReportingOptions _edgeOptions = edgeOptions.Value;
    private readonly IReadOnlyDictionary<string, IAcquisitionProtocolRunner> _protocolRunners =
        protocolRunners.ToDictionary(item => item.Protocol, StringComparer.Ordinal);
    private readonly Dictionary<string, Worker> _workers = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.SetEnabled(_localOptions.Enabled || CanLoadPlatformProfiles());
        var edgeId = identity.GetEdgeId();
        var platformAvailable = CanLoadPlatformProfiles();
        var canUseLocalFallback = _localOptions.Enabled &&
                                  (!platformAvailable || _localOptions.AllowLocalFallbackWhenPlatformAvailable);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var platformConfigurationLoaded = false;
                var cachedConfigurationLoaded = false;
                if (platformAvailable)
                {
                    try
                    {
                        var deployments = await LoadDeploymentsAsync(edgeId, stoppingToken).ConfigureAwait(false);
                        await SynchronizeWorkersAsync(
                            deployments,
                            edgeId,
                            AcquisitionConfigurationSources.Platform,
                            stoppingToken).ConfigureAwait(false);
                        platformConfigurationLoaded = true;
                        status.SetEnabled(true);
                        status.SetConfigurationError(
                            deployments.Count == 0
                                ? "平台没有为当前 Edge 发布采集配置，采集已停止。"
                                : null);
                        try
                        {
                            if (deployments.Count == 0 || status.AreDesiredDeploymentsApplied())
                                await deploymentCache.SaveAsync(edgeId, deployments, stoppingToken)
                                    .ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            logger.LogWarning(exception, "保存平台采集配置缓存失败，当前已加载的任务继续运行");
                        }
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "读取平台采集配置失败，继续运行上一次成功加载的版本");
                        status.SetConfigurationError("读取平台采集配置失败，正在使用最后一次成功版本。");
                    }
                }

                if (!platformConfigurationLoaded && _workers.Count == 0)
                {
                    var cached = await deploymentCache.LoadAsync(edgeId, stoppingToken)
                        .ConfigureAwait(false);
                    if (cached is not null)
                    {
                        await SynchronizeWorkersAsync(
                            cached,
                            edgeId,
                            AcquisitionConfigurationSources.Cache,
                            stoppingToken).ConfigureAwait(false);
                        cachedConfigurationLoaded = true;
                        status.SetEnabled(true);
                        status.SetConfigurationError(
                            platformAvailable
                                ? "平台暂时不可用，正在使用本地缓存的最后一次成功配置。"
                                : null);
                        logger.LogInformation(
                            "已从本地缓存恢复采集配置：EdgeId={EdgeId}, DeploymentCount={DeploymentCount}",
                            edgeId,
                            cached.Count);
                    }
                }

                if (!platformConfigurationLoaded &&
                    !cachedConfigurationLoaded &&
                    _workers.Count == 0 &&
                    canUseLocalFallback)
                {
                    StartWorker("local", _localOptions, edgeId, stoppingToken);
                    status.SetDesiredDeployments([], AcquisitionConfigurationSources.Local);
                    status.SetEnabled(true);
                    status.SetConfigurationError(null);
                    logger.LogWarning(
                        "当前 Edge 使用未版本化的本地采集配置；该模式仅适用于明确隔离的调试环境");
                }
                else if (platformConfigurationLoaded || cachedConfigurationLoaded || !canUseLocalFallback)
                    await StopWorkerAsync("local").ConfigureAwait(false);

                if (_workers.Count == 0 &&
                    !platformConfigurationLoaded &&
                    !cachedConfigurationLoaded &&
                    !canUseLocalFallback)
                {
                    status.SetEnabled(true);
                    status.SetConfigurationError(
                        "没有平台已发布配置或本地缓存，采集已停止；请为当前 Edge 发布数据连接配置。");
                }

                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await StopAllWorkersAsync().ConfigureAwait(false);
        }
    }

    private bool CanLoadPlatformProfiles()
        => _edgeOptions.IsPlatformReportingEnabled &&
           !string.IsNullOrWhiteSpace(_edgeOptions.EffectivePlatformApiBaseUrl);

    private async Task<IReadOnlyList<AcquisitionDeployment>> LoadDeploymentsAsync(
        string edgeId,
        CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("platform-acquisition-configuration");
        client.BaseAddress = new Uri(_edgeOptions.EffectivePlatformApiBaseUrl.TrimEnd('/') + "/");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/v1/acquisition-profiles/active?edgeId={Uri.EscapeDataString(edgeId)}");
        if (!string.IsNullOrWhiteSpace(_edgeOptions.EventIngestToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _edgeOptions.EventIngestToken);
        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<DeploymentEnvelope>(JsonOptions, ct)
            .ConfigureAwait(false);
        return payload?.Data ?? [];
    }

    internal async Task SynchronizeWorkersAsync(
        IReadOnlyList<AcquisitionDeployment> deployments,
        string edgeId,
        string configurationSource,
        CancellationToken stoppingToken)
    {
        status.SetDesiredDeployments(deployments, configurationSource);
        var activeProfileIds = deployments
            .Select(static item => item.Profile.ProfileId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in _workers
                     .Where(item => item.Key != "local" &&
                                    item.Value.Deployment is { } existing &&
                                    !activeProfileIds.Contains(existing.Profile.ProfileId))
                     .Select(static item => item.Key)
                     .ToArray())
            await StopWorkerAsync(key).ConfigureAwait(false);

        foreach (var deployment in deployments)
        {
            var key = DeploymentKey(deployment.Profile);
            if (_workers.ContainsKey(key)) continue;
            var previous = _workers
                .Where(item => item.Key != key &&
                               item.Value.Deployment?.Profile.ProfileId == deployment.Profile.ProfileId)
                .ToArray();
            try
            {
                status.RecordApplicationState(
                    deployment.Profile.ProfileId,
                    AcquisitionApplicationStates.Validating);
                if (!AcquisitionProfileValidator.TryValidate(
                        deployment.Profile,
                        out _,
                        out var validationError))
                    throw new InvalidDataException(validationError);

                if (configurationSource == AcquisitionConfigurationSources.Platform)
                {
                    var probe = await probeService.ProbeAsync(deployment, stoppingToken).ConfigureAwait(false);
                    if (!probe.Success || !probe.MappingsValidated)
                        throw new InvalidDataException(probe.Message);
                }

                var unsafeWorker = previous.FirstOrDefault(item => !status.IsSafeToReplace(item.Key));
                if (!string.IsNullOrWhiteSpace(unsafeWorker.Key))
                {
                    status.RecordApplicationState(
                        deployment.Profile.ProfileId,
                        AcquisitionApplicationStates.WaitingForCycleBoundary);
                    continue;
                }

                status.RecordApplicationState(
                    deployment.Profile.ProfileId,
                    AcquisitionApplicationStates.Applying);
                foreach (var previousWorker in previous)
                    await StopWorkerAsync(previousWorker.Key).ConfigureAwait(false);
                StartWorker(key, deployment, edgeId, stoppingToken);
                if (previous.Length > 0 &&
                    !await status.WaitForFirstSuccessAsync(
                        key,
                        TimeSpan.FromMilliseconds(Math.Clamp(
                            _localOptions.StartupHealthTimeoutMs,
                            1000,
                            300_000)),
                        stoppingToken).ConfigureAwait(false))
                {
                    var runtime = status.Get().Tasks.FirstOrDefault(item => item.ConfigurationKey == key);
                    throw new IOException(
                        runtime?.LastError is { Length: > 0 } error
                            ? $"候选采集配置未在启动健康期限内成功：{error}"
                            : "候选采集配置未在启动健康期限内产生成功采样。");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await StopWorkerAsync(key).ConfigureAwait(false);
                status.RecordApplicationState(
                    deployment.Profile.ProfileId,
                    previous.Length > 0
                        ? AcquisitionApplicationStates.Rollback
                        : AcquisitionApplicationStates.Failed,
                    exception.Message);
                foreach (var previousWorker in previous)
                {
                    if (_workers.ContainsKey(previousWorker.Key) || previousWorker.Value.Deployment is null)
                        continue;
                    try
                    {
                        StartWorker(
                            previousWorker.Key,
                            previousWorker.Value.Deployment,
                            edgeId,
                            stoppingToken);
                    }
                    catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
                    {
                        logger.LogCritical(
                            rollbackException,
                            "采集配置 {Configuration} 回滚启动失败",
                            previousWorker.Key);
                    }
                }
                logger.LogError(exception, "采集配置 {Configuration} 启动失败；其他采集任务继续运行", key);
            }
        }
    }

    private void StartWorker(
        string key,
        AcquisitionDeployment deployment,
        string edgeId,
        CancellationToken stoppingToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
            status.RegisterTask(key, deployment);
            Task task;
            if (deployment.Profile.Protocol == AcquisitionProtocols.HttpPolling)
            {
                var options = JsonAcquisitionOptionsFactory.Create(deployment);
                HttpPollingSnapshotMapper.ValidateOptions(options);
                task = RunWorkerAsync(key, options, edgeId, cancellation.Token);
            }
            else if (_protocolRunners.TryGetValue(deployment.Profile.Protocol, out var runner))
            {
                task = RunProtocolWorkerAsync(
                    runner,
                    key,
                    deployment,
                    NormalizeSource(edgeId, deployment.Profile.Source),
                    cancellation.Token);
            }
            else
            {
                throw new InvalidOperationException($"没有注册采集协议执行器：{deployment.Profile.Protocol}。");
            }
            _workers.Add(key, new Worker(cancellation, task, deployment));
        }
        catch
        {
            status.RemoveTask(key);
            cancellation.Dispose();
            throw;
        }
    }

    private async Task RunProtocolWorkerAsync(
        IAcquisitionProtocolRunner runner,
        string key,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await runner.RunAsync(key, deployment, normalizedSource, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                    throw new IOException($"采集协议执行器 {deployment.Profile.Protocol} 意外结束。");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                status.RecordFailure(key, exception.Message);
                logger.LogError(
                    exception,
                    "采集配置 {Configuration} 执行器异常退出；将在重连间隔后重试",
                    key);
                try
                {
                    await Task.Delay(deployment.Profile.Execution.ReconnectDelayMs, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private void StartWorker(
        string key,
        HttpPollingAcquisitionOptions options,
        string edgeId,
        CancellationToken stoppingToken)
    {
        HttpPollingSnapshotMapper.ValidateOptions(options);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        status.RegisterTask(key);
        var task = RunWorkerAsync(key, options, edgeId, cancellation.Token);
        _workers.Add(key, new Worker(cancellation, task, null));
    }

    private async Task StopWorkerAsync(string key)
    {
        if (!_workers.Remove(key, out var worker)) return;
        worker.Cancellation.Cancel();
        try
        {
            await worker.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "采集配置 {Configuration} 停止时发生异常", key);
        }
        finally
        {
            status.RemoveTask(key);
            worker.Cancellation.Dispose();
        }
    }

    internal async Task StopAllWorkersAsync()
    {
        foreach (var key in _workers.Keys.ToArray())
            await StopWorkerAsync(key).ConfigureAwait(false);
    }

    private async Task RunWorkerAsync(
        string key,
        HttpPollingAcquisitionOptions options,
        string edgeId,
        CancellationToken ct)
    {
        var source = NormalizeSource(edgeId, options.Source);
        var client = httpClientFactory.CreateClient($"acquisition:{key}");
        client.BaseAddress = new Uri(options.DeviceBaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, options.TimeoutMs));
        var delay = TimeSpan.FromMilliseconds(options.PollIntervalMs);
        string? currentRecipe = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();

        logger.LogInformation(
            "采集配置已运行：Configuration={Configuration}, Device={Device}, Subject={SubjectType}/{SubjectId}, PollDelayMs={PollDelayMs}, Fields={FieldCount}",
            key, client.BaseAddress, options.SubjectType, options.SubjectId, options.PollIntervalMs, options.Fields.Count);

        while (!ct.IsCancellationRequested)
        {
            var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            status.RecordAttempt(key, DateTimeOffset.UtcNow);
            try
            {
                using var response = await client.GetAsync(options.SnapshotPath.TrimStart('/'), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                    .ConfigureAwait(false);
                var mapped = HttpPollingSnapshotMapper.Map(snapshot, options, source, currentRecipe);
                if (!sourceDeduplicator.ShouldEmit(mapped.Sample))
                {
                    currentRecipe = mapped.RecipeIdentity;
                    status.RecordSuccess(
                        key,
                        DateTimeOffset.UtcNow,
                        currentRecipe,
                        incrementSample: false,
                        readDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    continue;
                }
                var events = lifecycle.Track(mapped, options.Lifecycle, options.PollIntervalMs);
                await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                status.RecordCycleState(key, lifecycle.IsRunActive);
                currentRecipe = mapped.RecipeIdentity;
                status.RecordSuccess(
                    key,
                    DateTimeOffset.UtcNow,
                    currentRecipe,
                    readDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(key, exception.Message);
                logger.LogWarning(exception, "采集配置 {Configuration} 读取设备失败；等待后重试", key);
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private static string DeploymentKey(AcquisitionProfile profile)
        => $"{profile.ProfileId}@{profile.Version}";

    private static string NormalizeSource(string edgeId, string source)
    {
        var trimmed = source.Trim().TrimStart('/');
        var prefix = $"edge/{edgeId}/";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{prefix}{trimmed}";
    }

    private sealed record DeploymentEnvelope(IReadOnlyList<AcquisitionDeployment> Data);
    private sealed record Worker(
        CancellationTokenSource Cancellation,
        Task Task,
        AcquisitionDeployment? Deployment);
}
