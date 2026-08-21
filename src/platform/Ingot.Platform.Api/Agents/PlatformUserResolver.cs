using System.Security.Claims;

namespace Ingot.Platform.Api.Agents;

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
                var siteIds = principal.FindAll(PlatformClaimTypes.SiteId)
                    .Select(static claim => claim.Value.Trim())
                    .Where(static siteId => !string.IsNullOrWhiteSpace(siteId))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                return new PlatformIdentity(value.Trim().ToLowerInvariant(), roles, siteIds);
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
                    StringComparer.Ordinal),
                new HashSet<string>(["*"], StringComparer.OrdinalIgnoreCase))
            : null;
    }
}
