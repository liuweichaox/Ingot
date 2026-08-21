using Ingot.Edge.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Edge.ConnectorHost.Controllers;

[ApiController]
[Route("api/logs")]
public class LogsController(ILogViewService logViewService) : ControllerBase
{

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? level = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? audience = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var skip = (page - 1) * pageSize;
            var (entries, totalCount) = await logViewService.GetLogsAsync(
                level, keyword, audience, skip, pageSize, cancellationToken);

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

    [HttpGet("levels")]
    public IActionResult GetLevels()
    {
        var levels = logViewService.GetAvailableLevels();
        return Ok(levels);
    }
}
