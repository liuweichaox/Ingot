using System.Security.Claims;

namespace Ingot.Platform.Api.Agents;

/// <summary>
///     从平台统一认证主体解析用户。开发环境允许固定本地身份，生产环境绝不信任客户端自报用户头。
/// </summary>
public sealed class PlatformUserResolver(IHostEnvironment environment)
{
    public string? Resolve(ClaimsPrincipal principal)
        => ResolveIdentity(principal)?.UserId;

    public PlatformIdentity? ResolveIdentity(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated == true)
        {
            var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? principal.Identity.Name;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var roles = principal.FindAll(ClaimTypes.Role)
                    .Concat(principal.FindAll("role"))
                    .Concat(principal.FindAll("roles"))
                    .Select(static claim => claim.Value.Trim().ToLowerInvariant())
                    .Where(static role => !string.IsNullOrWhiteSpace(role))
                    .ToHashSet(StringComparer.Ordinal);
                return new PlatformIdentity(value.Trim().ToLowerInvariant(), roles);
            }
        }

        return environment.IsDevelopment()
            ? new PlatformIdentity(
                "operator",
                new HashSet<string>(
                    [
                        PlatformRoles.QualityInspector,
                        PlatformRoles.QualityReviewer,
                        PlatformRoles.ProcessEngineer,
                        PlatformRoles.PlatformAdministrator
                    ],
                    StringComparer.Ordinal))
            : null;
    }
}
