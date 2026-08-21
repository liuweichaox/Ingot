using System.Security.Cryptography;
using Ingot.Contracts.Identity;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Identity;

public sealed class LocalAdminBootstrapper(
    NpgsqlDataSource dataSource,
    LocalPasswordHasher hasher,
    IOptions<LocalAuthOptions> options,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<LocalAdminBootstrapper> logger)
{
    private const long AdvisoryLockKey = 0x496E676F744964;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var mode = configuration["Authentication:Mode"] ?? "Local";
        if (environment.IsDevelopment() || !string.Equals(mode, "Local", StringComparison.OrdinalIgnoreCase))
            return;

        var username = string.IsNullOrWhiteSpace(options.Value.SeedAdminUsername)
            ? "admin"
            : options.Value.SeedAdminUsername.Trim();
        var password = options.Value.SeedAdminPassword;
        var generated = string.IsNullOrWhiteSpace(password);
        if (generated)
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var lockCommand = new NpgsqlCommand(
                         $"SELECT pg_advisory_xact_lock({AdvisoryLockKey});", connection, transaction))
            await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await using (var countCommand = new NpgsqlCommand("SELECT count(*) FROM users;", connection, transaction))
        {
            var count = Convert.ToInt64(await countCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (count > 0)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return;
            }
        }

        var now = DateTimeOffset.UtcNow;
        await using (var insert = new NpgsqlCommand(
                         """
                         INSERT INTO users(
                           user_id, username, username_lower, display_name, password_hash,
                           roles, site_ids, disabled, created_at, updated_at)
                         VALUES (
                           @user_id, @username, @username_lower, @display_name, @password_hash,
                           @roles, @site_ids, false, @created_at, @updated_at);
                         """,
                         connection,
                         transaction))
        {
            insert.Parameters.AddWithValue("user_id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("username", username);
            insert.Parameters.AddWithValue("username_lower", username.ToLowerInvariant());
            insert.Parameters.AddWithValue("display_name", "初始管理员");
            insert.Parameters.AddWithValue("password_hash", hasher.Hash(password!));
            insert.Parameters.AddWithValue("roles", PlatformRoleNames.All.ToArray());
            insert.Parameters.AddWithValue("site_ids", Array.Empty<string>());
            insert.Parameters.AddWithValue("created_at", now.UtcDateTime);
            insert.Parameters.AddWithValue("updated_at", now.UtcDateTime);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        if (generated)
            logger.LogCritical(
                "已创建初始管理员：用户名 {Username}，一次性口令 {Password}。请立即登录并修改口令；此口令仅本次显示。",
                username,
                password);
        else
            logger.LogInformation("已创建初始管理员：{Username}（口令来自配置）。", username);
    }
}
