using Ingot.Edge.ConnectorHost.Acquisition;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Edge.ConnectorHost.Controllers;

[ApiController]
[Route("api/v1/acquisition")]
public sealed class AcquisitionController(AcquisitionStatus status) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(status.Get());
}
