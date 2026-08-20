namespace Ingot.Platform.Application.Identity;

public sealed record UserAccount
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string UsernameLower { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public required string PasswordHash { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public IReadOnlyList<string> SiteIds { get; init; } = [];
    public bool Disabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ResolvedSession(
    Guid UserId,
    string Username,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> SiteIds);

/// <summary>管理本地身份模式下的用户、角色、站点范围和会话。</summary>
public interface ILocalUserStore
{
    Task<long> CountAsync(CancellationToken ct = default);
    Task<UserAccount> CreateAsync(UserAccount user, CancellationToken ct = default);
    Task<UserAccount?> GetByUsernameAsync(string usernameLower, CancellationToken ct = default);
    Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default);
    Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default);
    Task<bool> SetSiteAccessAsync(Guid userId, IReadOnlyList<string> siteIds, CancellationToken ct = default);
    Task<bool> SetDisabledAsync(Guid userId, bool disabled, CancellationToken ct = default);
    Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct = default);
    Task CreateSessionAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task<ResolvedSession?> ValidateSessionAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeSessionAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> PruneExpiredSessionsAsync(CancellationToken ct = default);
}
