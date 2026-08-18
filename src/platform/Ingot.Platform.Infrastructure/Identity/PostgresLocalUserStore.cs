using Npgsql;

namespace Ingot.Platform.Infrastructure.Identity;

/// <summary>已验证会话解析结果：足以构造 ClaimsPrincipal。</summary>
public sealed record ResolvedSession(Guid UserId, string Username, IReadOnlyList<string> Roles);

public interface ILocalUserStore
{
    Task<long> CountAsync(CancellationToken ct = default);
    Task<UserAccount> CreateAsync(UserAccount user, CancellationToken ct = default);
    Task<UserAccount?> GetByUsernameAsync(string usernameLower, CancellationToken ct = default);
    Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default);
    Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default);
    Task<bool> SetDisabledAsync(Guid userId, bool disabled, CancellationToken ct = default);
    Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct = default);

    Task CreateSessionAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task<ResolvedSession?> ValidateSessionAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeSessionAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
    Task<int> PruneExpiredSessionsAsync(CancellationToken ct = default);
}

/// <summary>本地账户与会话的 PostgreSQL 存储。schema 由迁移 0003 保证，本类不做 DDL。</summary>
public sealed class PostgresLocalUserStore : ILocalUserStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresLocalUserStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    private const string UserColumns =
        "user_id, username, username_lower, display_name, password_hash, roles, disabled, created_at, updated_at";

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("SELECT count(*) FROM users;");
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    public async Task<UserAccount> CreateAsync(UserAccount user, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            INSERT INTO users(user_id, username, username_lower, display_name, password_hash, roles, disabled, created_at, updated_at)
            VALUES (@user_id, @username, @username_lower, @display_name, @password_hash, @roles, @disabled, @created_at, @updated_at);
            """);
        command.Parameters.AddWithValue("user_id", user.UserId);
        command.Parameters.AddWithValue("username", user.Username);
        command.Parameters.AddWithValue("username_lower", user.UsernameLower);
        command.Parameters.AddWithValue("display_name", user.DisplayName);
        command.Parameters.AddWithValue("password_hash", user.PasswordHash);
        command.Parameters.AddWithValue("roles", user.Roles.ToArray());
        command.Parameters.AddWithValue("disabled", user.Disabled);
        command.Parameters.AddWithValue("created_at", user.CreatedAt.UtcDateTime);
        command.Parameters.AddWithValue("updated_at", user.UpdatedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return user;
    }

    public Task<UserAccount?> GetByUsernameAsync(string usernameLower, CancellationToken ct = default)
        => GetSingleAsync($"SELECT {UserColumns} FROM users WHERE username_lower = @key;", "key", usernameLower, ct);

    public Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken ct = default)
        => GetSingleAsync($"SELECT {UserColumns} FROM users WHERE user_id = @key;", "key", userId, ct);

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand($"SELECT {UserColumns} FROM users ORDER BY created_at;");
        var result = new List<UserAccount>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadUser(reader));
        return result;
    }

    public async Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "UPDATE users SET roles = @roles, updated_at = now() WHERE user_id = @user_id;");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("roles", roles.ToArray());
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<bool> SetDisabledAsync(Guid userId, bool disabled, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "UPDATE users SET disabled = @disabled, updated_at = now() WHERE user_id = @user_id;");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("disabled", disabled);
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected > 0 && disabled)
            await RevokeAllForUserAsync(userId, ct).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "UPDATE users SET password_hash = @hash, updated_at = now() WHERE user_id = @user_id;");
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("hash", passwordHash);
        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected > 0)
            await RevokeAllForUserAsync(userId, ct).ConfigureAwait(false); // 改密即注销其它会话
        return affected > 0;
    }

    public async Task CreateSessionAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            INSERT INTO user_sessions(token_hash, user_id, expires_at) VALUES (@token_hash, @user_id, @expires_at);
            """);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("expires_at", expiresAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<ResolvedSession?> ValidateSessionAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("""
            UPDATE user_sessions SET last_seen_at = now()
            WHERE token_hash = @token_hash AND expires_at > now()
            RETURNING user_id;
            """);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        var userIdObj = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (userIdObj is null or DBNull)
            return null;
        var user = await GetByIdAsync((Guid)userIdObj, ct).ConfigureAwait(false);
        return user is null || user.Disabled ? null : new ResolvedSession(user.UserId, user.Username, user.Roles);
    }

    public async Task RevokeSessionAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM user_sessions WHERE token_hash = @token_hash;");
        command.Parameters.AddWithValue("token_hash", tokenHash);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM user_sessions WHERE user_id = @user_id;");
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> PruneExpiredSessionsAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand("DELETE FROM user_sessions WHERE expires_at <= now();");
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<UserAccount?> GetSingleAsync(string sql, string paramName, object value, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(paramName, value);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadUser(reader) : null;
    }

    private static UserAccount ReadUser(NpgsqlDataReader reader) => new()
    {
        UserId = reader.GetGuid(0),
        Username = reader.GetString(1),
        UsernameLower = reader.GetString(2),
        DisplayName = reader.GetString(3),
        PasswordHash = reader.GetString(4),
        Roles = reader.GetFieldValue<string[]>(5),
        Disabled = reader.GetBoolean(6),
        CreatedAt = new DateTimeOffset(reader.GetDateTime(7).ToUniversalTime()),
        UpdatedAt = new DateTimeOffset(reader.GetDateTime(8).ToUniversalTime())
    };

}
