using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Ingot.Domain.Events;
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
    private readonly EdgeDeliveryStatus _deliveryStatus;
    private readonly EdgeReportingOptions _options;

    public HttpEventShipper(
        IEventLog eventLog,
        IEdgeIdentityProvider identity,
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeReportingOptions> options,
        IMetricsCollector metrics,
        EdgeDeliveryStatus deliveryStatus,
        ILogger<HttpEventShipper> logger)
    {
        _eventLog = eventLog;
        _identity = identity;
        _httpClientFactory = httpClientFactory;
        _metrics = metrics;
        _deliveryStatus = deliveryStatus;
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
        if (string.IsNullOrWhiteSpace(_options.PlatformApiBaseUrl))
            throw new InvalidOperationException("启用事件上行时必须配置 Edge:PlatformApiBaseUrl。");
        if (string.IsNullOrWhiteSpace(_options.SiteId))
            throw new InvalidOperationException("启用事件上行时必须配置 Edge:SiteId。");
        if (string.IsNullOrWhiteSpace(_options.EventIngestToken))
            throw new InvalidOperationException("启用事件上行时必须配置 Edge:EventIngestToken。");

        var edgeId = _identity.GetEdgeId();
        var siteId = _options.SiteId.Trim();
        var http = _httpClientFactory.CreateClient(nameof(HttpEventShipper));
        http.BaseAddress = new Uri(_options.PlatformApiBaseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.EventIngestToken);

        var batchSize = Math.Clamp(_options.EventBatchSize, 1, 500);
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(100, _options.EventIdleDelayMs));
        var maxRetry = TimeSpan.FromSeconds(Math.Max(1, _options.EventRetryMaxSeconds));
        var retry = TimeSpan.FromSeconds(1);

        _logger.LogInformation(
            "事件上行已启动：SiteId={SiteId}, EdgeId={EdgeId}, Platform={Platform}, BatchSize={BatchSize}",
            siteId,
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
                    SiteId = siteId,
                    EdgeId = edgeId,
                    Events = pending
                };
                using var response = await http.PostAsJsonAsync(
                        PlatformEventRoutes.BatchIngest,
                        request,
                        JsonOptions,
                        ct)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content
                        .ReadAsStringAsync(ct)
                        .ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        var reason =
                            $"中心报告不可恢复的 Edge 身份或序号冲突（HTTP 409）：{responseBody}";
                        _deliveryStatus.RecordBlocked(reason, DateTimeOffset.UtcNow);
                        _logger.LogCritical(
                            "事件上行已阻塞，自动重试已停止。请检查 outbox；若 outbox 已重建，必须更换 EdgeId。" +
                            " EdgeId={EdgeId}, Detail={Detail}",
                            edgeId,
                            responseBody);
                        return;
                    }
                    if (IsDeterministicPayloadRejection(response.StatusCode))
                    {
                        await IsolateRejectedEventsAsync(http, siteId, edgeId, pending, responseBody, ct)
                            .ConfigureAwait(false);
                        retry = TimeSpan.FromSeconds(1);
                        continue;
                    }
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
                _deliveryStatus.RecordSuccess(
                    result.AckSeq,
                    confirmed,
                    DateTimeOffset.UtcNow,
                    stopwatch.Elapsed.TotalMilliseconds);
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
                _deliveryStatus.RecordFailure(ex.Message, DateTimeOffset.UtcNow);
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

    private async Task IsolateRejectedEventsAsync(
        HttpClient http,
        string siteId,
        string edgeId,
        IReadOnlyList<ProductionEvent> pending,
        string batchError,
        CancellationToken ct)
    {
        foreach (var evt in pending)
        {
            var request = new EventBatchRequest { SiteId = siteId, EdgeId = edgeId, Events = [evt] };
            using var response = await http.PostAsJsonAsync(
                PlatformEventRoutes.BatchIngest, request, JsonOptions, ct).ConfigureAwait(false);
            if (IsDeterministicPayloadRejection(response.StatusCode))
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                await _eventLog.QuarantineAsync(
                    evt.Seq,
                    $"HTTP {(int)response.StatusCode}: {detail}",
                    ct).ConfigureAwait(false);
                _logger.LogError(
                    "已隔离被中心确定性拒绝的事件：Seq={Seq}, EventId={EventId}, BatchError={BatchError}",
                    evt.Seq,
                    evt.EventId,
                    batchError);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"单条事件重试失败（HTTP {(int)response.StatusCode}）：{detail}",
                    null,
                    response.StatusCode);
            }
            var result = await response.Content.ReadFromJsonAsync<EventBatchResponse>(JsonOptions, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("中心返回了空的事件确认响应。");
            if (result.AckSeq != evt.Seq)
                throw new InvalidDataException($"单条事件确认序号错误：Ack={result.AckSeq}, Seq={evt.Seq}");
            await _eventLog.MarkShippedAsync(evt.Seq, ct).ConfigureAwait(false);
        }
        await RecordBacklogMetricAsync(ct).ConfigureAwait(false);
    }

    private static bool IsDeterministicPayloadRejection(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;

    private async Task RecordBacklogMetricAsync(CancellationToken ct)
    {
        try
        {
            var statistics = await _eventLog.GetPendingStatisticsAsync(ct).ConfigureAwait(false);
            _deliveryStatus.RecordBacklog(statistics);
            _metrics.RecordEventOutboxBacklog(statistics.Count);
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
