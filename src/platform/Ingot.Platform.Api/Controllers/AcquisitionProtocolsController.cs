using Ingot.Contracts.Acquisition;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/acquisition-protocols")]
public sealed class AcquisitionProtocolsController(PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public IActionResult List()
    {
        if (userResolver.ResolveIdentity(User) is null)
            return AuthenticationRequired("需要平台统一认证。");
        return Ok(new
        {
            protocols = AcquisitionProtocolCapabilities.All,
            melsecDevices = AcquisitionSelectors.MelsecDeviceCatalog
                .OrderBy(static item => item.Code, StringComparer.Ordinal)
                .Select(static item => new
                {
                    code = item.Code,
                    isBitDevice = item.IsBitDevice,
                    radix = item.Radix,
                    description = item.Description
                }),
            modbusAreas = AcquisitionSelectors.ModbusAreaValues
        });
    }
}
