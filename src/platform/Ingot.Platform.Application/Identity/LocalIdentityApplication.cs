namespace Ingot.Platform.Application.Identity;

public sealed class LocalIdentityApplication(ILocalUserStore users)
{
    public Task<UserAccount> CreateAsync(UserAccount user, CancellationToken ct = default)
        => users.CreateAsync(user, ct);
    public Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => users.GetByUsernameAsync(username, ct);
    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default)
        => users.ListAsync(ct);
    public Task<bool> SetRolesAsync(Guid id, IReadOnlyList<string> roles, CancellationToken ct = default)
        => users.SetRolesAsync(id, roles, ct);
    public Task<bool> SetSiteAccessAsync(Guid id, IReadOnlyList<string> sites, CancellationToken ct = default)
        => users.SetSiteAccessAsync(id, sites, ct);
    public Task<bool> SetPasswordHashAsync(Guid id, string passwordHash, CancellationToken ct = default)
        => users.SetPasswordHashAsync(id, passwordHash, ct);
    public Task<bool> SetDisabledAsync(Guid id, bool disabled, CancellationToken ct = default)
        => users.SetDisabledAsync(id, disabled, ct);
    public Task CreateSessionAsync(
        string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default)
        => users.CreateSessionAsync(tokenHash, userId, expiresAt, ct);
    public Task RevokeSessionAsync(string tokenHash, CancellationToken ct = default)
        => users.RevokeSessionAsync(tokenHash, ct);
}
