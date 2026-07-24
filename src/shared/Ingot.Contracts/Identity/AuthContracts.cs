namespace Ingot.Contracts.Identity;

/// <summary>
///     平台角色的规范字符串常量（跨项目共享）。Api 的 PlatformRoles 与本地账户播种共用同一取值，
///     避免角色字符串在多处漂移。
/// </summary>
public static class PlatformRoleNames
{
    public const string QualityInspector = "quality.inspector";
    public const string QualityReviewer = "quality.reviewer";
    public const string ProcessEngineer = "process.engineer";
    public const string PlatformAdministrator = "platform.admin";

    public static readonly IReadOnlyList<string> All =
        [QualityInspector, QualityReviewer, ProcessEngineer, PlatformAdministrator];

    public static bool IsKnown(string role) => All.Contains(role);
}

public sealed record LoginRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
}

public sealed record LoginResponse
{
    public required string Token { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed record IdentityResponse
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public required IReadOnlyList<string> Roles { get; init; }
}

public sealed record UserSummary
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public required IReadOnlyList<string> Roles { get; init; }
    public bool Disabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateUserRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record SetRolesRequest
{
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record SetPasswordRequest
{
    public string? Password { get; init; }
}

public sealed record SetDisabledRequest
{
    public bool Disabled { get; init; }
}
