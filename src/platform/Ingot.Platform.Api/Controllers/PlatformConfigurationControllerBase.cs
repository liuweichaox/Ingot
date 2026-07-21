using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

public abstract class PlatformConfigurationControllerBase(
    PlatformUserResolver userResolver) : ControllerBase
{
    protected IActionResult? DeniedConfigurationRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : Forbid();
    }

    protected IActionResult? DeniedConfigurationWrite()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? null
            : Forbid();
    }
}
