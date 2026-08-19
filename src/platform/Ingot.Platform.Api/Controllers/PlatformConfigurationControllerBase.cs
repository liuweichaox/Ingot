using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

public abstract class PlatformConfigurationControllerBase(
    PlatformUserResolver userResolver) : PlatformApiController
{
    protected string? ResolveUserId() => userResolver.Resolve(User);

    protected PlatformIdentity? ResolveIdentity() => userResolver.ResolveIdentity(User);

    protected IActionResult? DeniedConfigurationRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : AuthorizationDenied();
    }

    protected IActionResult? DeniedConfigurationWrite()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? null
            : AuthorizationDenied();
    }

    protected IActionResult? DeniedResearchAssetRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? null
            : AuthorizationDenied();
    }
}
