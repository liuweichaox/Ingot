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
public sealed class HttpPollingAcquisitionHostedService(
    IHttpClientFactory httpClientFactory,
    IEventSink sink,
    IEdgeIdentityProvider identity,
    IOptions<HttpPollingAcquisitionOptions> configuredOptions,
    IOptions<EdgeReportingOptions> edgeOptions,
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

        if (!platformAvailable && !_localOptions.Enabled)
        {
            logger.LogInformation("当前边缘节点没有启用采集，也未配置平台采集配置地址");
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var receivedPlatformConfiguration = false;
                if (platformAvailable)
                {
                    try
                    {
                        var deployments = await LoadDeploymentsAsync(edgeId, stoppingToken).ConfigureAwait(false);
                        receivedPlatformConfiguration = deployments.Count > 0;
                        SynchronizeWorkers(deployments, edgeId, stoppingToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception, "读取平台采集配置失败，继续运行上一次成功加载的版本");
                    }
                }

                if (!receivedPlatformConfiguration && _workers.Count == 0 && _localOptions.Enabled)
                    StartWorker("local", _localOptions, edgeId, stoppingToken);
                else if (receivedPlatformConfiguration)
                    StopWorker("local");

                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var worker in _workers.Values) worker.Cancellation.Cancel();
            await Task.WhenAll(_workers.Values.Select(worker => worker.Task)).ConfigureAwait(false);
            foreach (var worker in _workers.Values) worker.Cancellation.Dispose();
            _workers.Clear();
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

    private void SynchronizeWorkers(
        IReadOnlyList<AcquisitionDeployment> deployments,
        string edgeId,
        CancellationToken stoppingToken)
    {
        var activeKeys = deployments
            .Select(item => DeploymentKey(item.Profile))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in _workers.Keys.Where(key => key != "local" && !activeKeys.Contains(key)).ToArray())
            StopWorker(key);

        foreach (var deployment in deployments)
        {
            var key = DeploymentKey(deployment.Profile);
            if (_workers.ContainsKey(key)) continue;
            StartWorker(key, deployment, edgeId, stoppingToken);
        }
    }

    private void StartWorker(
        string key,
        AcquisitionDeployment deployment,
        string edgeId,
        CancellationToken stoppingToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        status.RegisterTask(key);
        Task task;
        if (deployment.Profile.Protocol == AcquisitionProtocols.HttpPolling)
        {
            var options = JsonAcquisitionOptionsFactory.Create(deployment);
            HttpPollingSnapshotMapper.ValidateOptions(options);
            task = RunWorkerAsync(key, options, edgeId, cancellation.Token);
        }
        else if (_protocolRunners.TryGetValue(deployment.Profile.Protocol, out var runner))
        {
            task = runner.RunAsync(
                key,
                deployment,
                NormalizeSource(edgeId, deployment.Profile.Source),
                cancellation.Token);
        }
        else
        {
            cancellation.Dispose();
            status.RemoveTask(key);
            throw new InvalidOperationException($"没有注册采集协议执行器：{deployment.Profile.Protocol}。");
        }
        _workers.Add(key, new Worker(cancellation, task));
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
        _workers.Add(key, new Worker(cancellation, task));
    }

    private void StopWorker(string key)
    {
        if (!_workers.Remove(key, out var worker)) return;
        worker.Cancellation.Cancel();
        status.RemoveTask(key);
        _ = worker.Task.ContinueWith(
            _ => worker.Cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
        client.Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, options.PollIntervalMs * 3));
        var delay = TimeSpan.FromMilliseconds(options.PollIntervalMs);
        string? currentRecipe = null;
        var lifecycle = new AcquisitionLifecycleTracker();

        logger.LogInformation(
            "采集配置已运行：Configuration={Configuration}, Device={Device}, Subject={SubjectType}/{SubjectId}, PeriodMs={PeriodMs}, Fields={FieldCount}",
            key, client.BaseAddress, options.SubjectType, options.SubjectId, options.PollIntervalMs, options.Fields.Count);

        while (!ct.IsCancellationRequested)
        {
            status.RecordAttempt(key, DateTimeOffset.UtcNow);
            try
            {
                using var response = await client.GetAsync(options.SnapshotPath.TrimStart('/'), ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var snapshot = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)
                    .ConfigureAwait(false);
                var mapped = HttpPollingSnapshotMapper.Map(snapshot, options, source, currentRecipe);
                foreach (var productionEvent in lifecycle.Track(mapped, options.Lifecycle, options.SamplePeriodMs))
                    await sink.EmitAsync(productionEvent, ct).ConfigureAwait(false);
                currentRecipe = mapped.RecipeIdentity;
                status.RecordSuccess(key, DateTimeOffset.UtcNow, currentRecipe);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(key, exception.Message);
                logger.LogWarning(exception, "采集配置 {Configuration} 读取设备失败；下个采集周期重试", key);
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
    private sealed record Worker(CancellationTokenSource Cancellation, Task Task);
}
