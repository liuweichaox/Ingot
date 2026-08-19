using Ingot.Contracts.Acquisition;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

/// <summary>
///     公开采集驱动的能力矩阵。
///
///     配置界面据此决定显示哪些字段：以前"连接超时"这类字段对全部协议一律显示，
///     而 5 个驱动里只有 2 个真正读取它，工程师填了一个不会生效的值也不会得到任何提示。
///     能力由后端声明、界面跟随，避免两边各自维护一份关于"哪个字段有效"的记忆。
/// </summary>
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
