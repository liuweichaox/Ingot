using Ingot.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Connector.Host.Controllers;

/// <summary>
///     日志查询 API（供 Web 门户代理调用）
/// </summary>
[ApiController]
[Route("api/logs")]
public class LogsController(ILogViewService logViewService) : ControllerBase
{
    /// <summary>
    ///     获取日志数据
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skip = (page - 1) * pageSize;
            var (entries, totalCount) = await logViewService.GetLogsAsync(
                level, keyword, skip, pageSize, cancellationToken);

            return Ok(new
            {
                Data = entries,
                Total = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    ///     获取可用的日志级别
    /// </summary>
    [HttpGet("levels")]
    public IActionResult GetLevels()
    {
        var levels = logViewService.GetAvailableLevels();
        return Ok(levels);
    }
}

