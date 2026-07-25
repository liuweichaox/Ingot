using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

public abstract class PlatformConfigurationControllerBase(
    PlatformUserResolver userResolver) : ControllerBase
{
    protected string? ResolveUserId() => userResolver.Resolve(User);

    protected PlatformIdentity? ResolveIdentity() => userResolver.ResolveIdentity(User);

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

    protected IActionResult? DeniedResearchAssetRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? null
            : Forbid();
    }
}
