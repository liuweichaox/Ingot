// 定义平台角色、站点声明和站点范围解析规则。
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

public enum SiteScopeFailure
{
    None,
    Missing,
    Forbidden
}

public static class PlatformSiteScope
{
    public static SiteScopeFailure Resolve(
        PlatformIdentity identity,
        string? requestedSiteId,
        bool allowAllForAdministrator,
        out string? siteId)
    {
        siteId = requestedSiteId?.Trim();
        if (!string.IsNullOrWhiteSpace(siteId))
        {
            if (identity.Roles.Contains(PlatformRoles.PlatformAdministrator))
                return SiteScopeFailure.None;
            var normalizedRequestedSiteId = siteId;
            var canonicalSiteId = identity.SiteIds.FirstOrDefault(value =>
                string.Equals(value, normalizedRequestedSiteId, StringComparison.OrdinalIgnoreCase));
            if (canonicalSiteId is null)
                return SiteScopeFailure.Forbidden;
            siteId = canonicalSiteId;
            return SiteScopeFailure.None;
        }
        if (allowAllForAdministrator && identity.Roles.Contains(PlatformRoles.PlatformAdministrator))
        {
            siteId = null;
            return SiteScopeFailure.None;
        }
        if (identity.SiteIds.Count == 1)
        {
            siteId = identity.SiteIds.Single();
            return SiteScopeFailure.None;
        }
        return SiteScopeFailure.Missing;
    }
}
