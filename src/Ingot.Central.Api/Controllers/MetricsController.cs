using Ingot.Central.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

/// <summary>
///     指标查看控制器
/// </summary>
[ApiController]
[Route("api/metrics-data")]
public class MetricsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MetricsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    ///     获取格式化的指标数据（JSON 格式）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMetricsJson()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var response = await client.GetStringAsync($"{baseUrl}/metrics");

            var metrics = PrometheusTextParser.Parse(response);

            return Ok(new
            {
                timestamp = DateTimeOffset.UtcNow,
                metrics
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    ///     获取指标信息（说明如何查看指标）
    /// </summary>
    [HttpGet("info")]
    public IActionResult GetMetricsInfo()
    {
        return Ok(new
        {
            message = "指标数据查看方式",
            endpoints = new
            {
                json = "/api/metrics-data - JSON 格式的指标数据（推荐）",
                prometheus = "/metrics - Prometheus 原始格式"
            },
            availableMetrics = new[]
            {
                "ingot_telemetry_collection_latency_ms - 采集延迟（毫秒）",
                "ingot_telemetry_collection_rate - 采集频率（points/s）",
                "ingot_telemetry_queue_depth - 队列深度（Channel待读取 + 批量积累，消息数）",
                "ingot_telemetry_processing_latency_ms - 处理延迟（毫秒）",
                "ingot_telemetry_write_latency_ms - 写入延迟（毫秒）",
                "ingot_telemetry_batch_write_efficiency - 批量写入效率（points/ms）",
                "ingot_telemetry_errors_total - 错误总数",
                "ingot_source_connection_status_changes_total - 连接状态变化总数",
                "ingot_source_connection_duration_seconds - 连接持续时间（秒）"
            }
        });
    }
}
