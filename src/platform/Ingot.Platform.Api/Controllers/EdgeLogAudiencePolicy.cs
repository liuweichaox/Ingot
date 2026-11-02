// Enforce the role boundary for proxied Edge log audiences.
using Ingot.Platform.Api.Agents;

namespace Ingot.Platform.Api.Controllers;

public static class EdgeLogAudiencePolicy
{
    private const string OperatorAudience = "operator";

    public static string? Resolve(PlatformIdentity identity, string? requestedAudience)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.HasAnyRole(PlatformRoles.PlatformAdministrator))
            return OperatorAudience;

        return string.IsNullOrWhiteSpace(requestedAudience) ? null : requestedAudience;
    }
}
