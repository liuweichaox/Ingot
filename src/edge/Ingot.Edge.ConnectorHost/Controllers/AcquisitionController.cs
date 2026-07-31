using Ingot.Edge.ConnectorHost.Acquisition;
using Ingot.Contracts.Acquisition;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Edge.ConnectorHost.Controllers;

[ApiController]
[Route("api/v1/acquisition")]
public sealed class AcquisitionController(
    AcquisitionStatus status,
    AcquisitionProbeService probeService) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(status.Get());

    [HttpPost("probe")]
    public async Task<IActionResult> Probe(
        [FromBody] AcquisitionProbeRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Deployment is null)
            return BadRequest(new { error = "缺少待验证的采集配置。" });
        try
        {
            return Ok(await probeService.ProbeAsync(request.Deployment, cancellationToken)
                .ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "设备连接或样本读取超时。" });
        }
        catch (Exception exception)
        {
            return BadRequest(new { error = $"设备探查失败：{exception.Message}" });
        }
    }
}
