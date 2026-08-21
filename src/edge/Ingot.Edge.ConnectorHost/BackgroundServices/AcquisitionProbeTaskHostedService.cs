using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Ingot.Edge.ConnectorHost.Acquisition;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.ConnectorHost.BackgroundServices;

public sealed class AcquisitionProbeTaskHostedService(
    IHttpClientFactory httpClientFactory,
    IEdgeIdentityProvider identity,
    IOptions<EdgeReportingOptions> options,
    AcquisitionProbeService probeService,
    ILogger<AcquisitionProbeTaskHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EdgeReportingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnablePlatformReporting ||
            string.IsNullOrWhiteSpace(_options.PlatformApiBaseUrl))
            return;

        var edgeId = identity.GetEdgeId();
        var client = httpClientFactory.CreateClient("platform-acquisition-probe-tasks");
        client.BaseAddress = new Uri(_options.PlatformApiBaseUrl.TrimEnd('/') + "/");
        if (!string.IsNullOrWhiteSpace(_options.EventIngestToken))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.EventIngestToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            AcquisitionProbeTask? task = null;
            try
            {
                using var response = await client.GetAsync(
                    $"api/v1/ingestion-tasks/probe-tasks/next?edgeId={Uri.EscapeDataString(edgeId)}",
                    stoppingToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                task = await response.Content.ReadFromJsonAsync<AcquisitionProbeTask>(
                    JsonOptions,
                    stoppingToken).ConfigureAwait(false);
                if (task is null)
                    throw new InvalidDataException("Platform returned an empty acquisition probe task.");

                AcquisitionProbeResult result;
                try
                {
                    result = await probeService.ProbeAsync(
                        task.Deployment,
                        task.Discovery,
                        stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    result = new AcquisitionProbeResult
                    {
                        Success = false,
                        MappingsValidated = false,
                        Protocol = task.Deployment.Task.Protocol,
                        Message = "设备连接或样本读取超时。",
                        TestedAt = DateTimeOffset.UtcNow
                    };
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = new AcquisitionProbeResult
                    {
                        Success = false,
                        MappingsValidated = false,
                        Protocol = task.Deployment.Task.Protocol,
                        Message = $"设备探查失败：{exception.Message}",
                        TestedAt = DateTimeOffset.UtcNow
                    };
                }

                await ReportCompletionWithRetryAsync(
                    client,
                    new AcquisitionProbeTaskCompletion
                    {
                        TaskId = task.TaskId,
                        EdgeId = edgeId,
                        Result = result
                    },
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "拉取或回报采集探查任务失败：TaskId={TaskId}；稍后重试",
                    task?.TaskId);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task ReportCompletionWithRetryAsync(
        HttpClient client,
        AcquisitionProbeTaskCompletion completion,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await client.PostAsJsonAsync(
                    $"api/v1/ingestion-tasks/probe-tasks/{Uri.EscapeDataString(completion.TaskId)}/result",
                    completion,
                    JsonOptions,
                    ct).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }
    }
}
