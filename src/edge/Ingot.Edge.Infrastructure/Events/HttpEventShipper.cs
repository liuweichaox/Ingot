using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Ingot.Contracts.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.Infrastructure.Events;

/// <summary>
///     从最小未确认 Seq 开始批量上行。HTTP 超时或响应丢失时安全重发，
    ///     Platform 通过 EventId 与 (EdgeId, Seq) 去重。
/// </summary>
public sealed class HttpEventShipper : IEventShipper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IEventLog _eventLog;
    private readonly IEdgeIdentityProvider _identity;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpEventShipper> _logger;
    private readonly IMetricsCollector _metrics;
    private readonly EdgeReportingOptions _options;

    public HttpEventShipper(
        IEventLog eventLog,
        IEdgeIdentityProvider identity,
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeReportingOptions> options,
        IMetricsCollector metrics,
        ILogger<HttpEventShipper> logger)
    {
        _eventLog = eventLog;
        _identity = identity;
        _httpClientFactory = httpClientFactory;
        _metrics = metrics;
        _logger = logger;
        _options = options.Value;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!_options.EnableEventShipping)
        {
            _logger.LogInformation("已禁用事件上行（Edge:EnableEventShipping=false）");
            return;
        }
        if (string.IsNullOrWhiteSpace(_options.EffectivePlatformApiBaseUrl))
            throw new InvalidOperationException("启用事件上行时必须配置 Edge:PlatformApiBaseUrl。");
        if (string.IsNullOrWhiteSpace(_options.EventIngestToken))
            throw new InvalidOperationException("启用事件上行时必须配置 Edge:EventIngestToken。");

        var edgeId = _identity.GetEdgeId();
        var http = _httpClientFactory.CreateClient(nameof(HttpEventShipper));
        http.BaseAddress = new Uri(_options.EffectivePlatformApiBaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.EventIngestToken);

        var batchSize = Math.Clamp(_options.EventBatchSize, 1, 500);
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(100, _options.EventIdleDelayMs));
        var maxRetry = TimeSpan.FromSeconds(Math.Max(1, _options.EventRetryMaxSeconds));
        var retry = TimeSpan.FromSeconds(1);

        _logger.LogInformation(
            "事件上行已启动：EdgeId={EdgeId}, Platform={Platform}, BatchSize={BatchSize}",
            edgeId,
            http.BaseAddress,
            batchSize);

        while (!ct.IsCancellationRequested)
        {
            var pending = await _eventLog.ReadPendingAsync(batchSize, ct).ConfigureAwait(false);
            await RecordBacklogMetricAsync(ct).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                retry = TimeSpan.FromSeconds(1);
                await Task.Delay(idleDelay, ct).ConfigureAwait(false);
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var request = new EventBatchRequest
                {
                    EdgeId = edgeId,
                    Events = pending
                };
                using var response = await http.PostAsJsonAsync(
                        "api/v1/events:batch",
                        request,
                        JsonOptions,
                        ct)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content
                        .ReadAsStringAsync(ct)
                        .ConfigureAwait(false);
                    throw new HttpRequestException(
                        $"中心拒绝事件批次（HTTP {(int)response.StatusCode}）：{responseBody}",
                        null,
                        response.StatusCode);
                }
                var result = await response.Content
                    .ReadFromJsonAsync<EventBatchResponse>(JsonOptions, ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException("中心返回了空的事件确认响应。");

                var firstSeq = pending[0].Seq;
                var lastSeq = pending[^1].Seq;
                if (result.AckSeq < firstSeq || result.AckSeq > lastSeq)
                    throw new InvalidDataException(
                        $"中心 AckSeq 超出当前批次范围：Ack={result.AckSeq}, Batch={firstSeq}-{lastSeq}");

                await _eventLog.MarkShippedAsync(result.AckSeq, ct).ConfigureAwait(false);
                stopwatch.Stop();
                var confirmed = pending.Count(evt => evt.Seq <= result.AckSeq);
                RecordEventsShippedMetric(
                    edgeId,
                    confirmed,
                    stopwatch.Elapsed.TotalMilliseconds);
                await RecordBacklogMetricAsync(ct).ConfigureAwait(false);
                retry = TimeSpan.FromSeconds(1);

                _logger.LogDebug(
                    "事件批次已确认：EdgeId={EdgeId}, Accepted={Accepted}, Duplicates={Duplicates}, AckSeq={AckSeq}",
                    edgeId,
                    result.Accepted,
                    result.Duplicates,
                    result.AckSeq);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await _eventLog
                    .IncrementShipAttemptsAsync(pending[0].Seq, pending[^1].Seq, ct)
                    .ConfigureAwait(false);
                RecordShipFailureMetric(edgeId);
                _logger.LogWarning(
                    ex,
                    "事件上行失败，将在 {RetrySeconds}s 后从未确认 Seq 重试",
                    retry.TotalSeconds);
                await Task.Delay(retry, ct).ConfigureAwait(false);
                retry = TimeSpan.FromSeconds(Math.Min(maxRetry.TotalSeconds, retry.TotalSeconds * 2));
            }
        }
    }

    private async Task RecordBacklogMetricAsync(CancellationToken ct)
    {
        try
        {
            _metrics.RecordEventOutboxBacklog(
                await _eventLog.CountPendingAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "读取或记录事件 outbox backlog 指标失败；事件上行继续运行。");
        }
    }

    private void RecordEventsShippedMetric(string edgeId, int count, double latencyMs)
    {
        try
        {
            _metrics.RecordEventsShipped(edgeId, count, latencyMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "中心已经确认事件，但记录上行成功指标失败。");
        }
    }

    private void RecordShipFailureMetric(string edgeId)
    {
        try
        {
            _metrics.RecordEventShipFailure(edgeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "事件上行失败，且记录失败指标时发生异常。");
        }
    }
}
