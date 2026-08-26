// 定义本地登录与认证配置的有界请求响应契约。
namespace Ingot.Contracts.Identity;

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
    public IReadOnlyList<string> SiteIds { get; init; } = [];
}

public sealed record AuthConfigurationResponse
{
    public required string Mode { get; init; }
    public string? Authority { get; init; }
    public string? ClientId { get; init; }
    public string Scope { get; init; } = "openid profile";
    public string CallbackPath { get; init; } = "/auth/callback";
    public string SilentCallbackPath { get; init; } = "/auth/silent-callback";
    public string LogoutCallbackPath { get; init; } = "/auth/logout-callback";
}

public sealed record IdentityResponse
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public required IReadOnlyList<string> Roles { get; init; }
    public IReadOnlyList<string> SiteIds { get; init; } = [];
}

public sealed record UserSummary
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public required IReadOnlyList<string> Roles { get; init; }
    public IReadOnlyList<string> SiteIds { get; init; } = [];
    public bool Disabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateUserRequest
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> SiteIds { get; init; } = [];
}

public sealed record SetRolesRequest
{
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record SetSiteAccessRequest
{
    public IReadOnlyList<string> SiteIds { get; init; } = [];
}

public sealed record SetPasswordRequest
{
    public string? Password { get; init; }
}

public sealed record SetDisabledRequest
{
    public bool Disabled { get; init; }
}
