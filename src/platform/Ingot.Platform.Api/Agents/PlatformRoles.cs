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

public sealed record PlatformIdentity(string UserId, IReadOnlySet<string> Roles)
{
    public bool HasAnyRole(params string[] roles)
        => roles.Any(Roles.Contains);
}
