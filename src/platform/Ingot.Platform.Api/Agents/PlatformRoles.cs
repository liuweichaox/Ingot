namespace Ingot.Platform.Api.Agents;

public static class PlatformRoles
{
    public const string QualityInspector = "quality.inspector";
    public const string QualityReviewer = "quality.reviewer";
    public const string ProcessEngineer = "process.engineer";
    public const string PlatformAdministrator = "platform.admin";

    public static readonly string[] QualityRead =
    [
        QualityInspector,
        QualityReviewer,
        ProcessEngineer,
        PlatformAdministrator
    ];
}

public static class PlatformClaimTypes
{
    public const string SiteId = "ingot:site";
}

public sealed record PlatformIdentity(
    string UserId,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> SiteIds)
{
    public bool HasAnyRole(params string[] roles)
        => roles.Any(Roles.Contains);

    public bool CanAccessSite(string? siteId)
        => !string.IsNullOrWhiteSpace(siteId) &&
           (Roles.Contains(PlatformRoles.PlatformAdministrator) || SiteIds.Contains(siteId.Trim()));
}
