using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Ingot.Platform.Infrastructure.Identity;

public sealed class LocalAuthOptions
{
    public int SessionLifetimeHours { get; set; } = 12;

    /// <summary>首次启动且无任何用户时的初始管理员；缺省则生成随机口令并写入日志。</summary>
    public string? SeedAdminUsername { get; set; }
    public string? SeedAdminPassword { get; set; }
}

/// <summary>
///     口令哈希：直接复用 ASP.NET Core 的 PasswordHasher（PBKDF2-HMAC-SHA256，加盐、可升级迭代）。
///     不自研密码学。令牌哈希用 SHA-256（令牌本身是高熵随机串，无需加盐）。
/// </summary>
public sealed class LocalPasswordHasher
{
    private static readonly object Dummy = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password)
        => _hasher.VerifyHashedPassword(Dummy, hash, password) != PasswordVerificationResult.Failed;

    public static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string HashToken(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
///     登录失败限速：单实例内存计数。同一用户名在窗口内失败达上限后暂时拒绝，减缓暴力破解。
///     成功登录清零。不替代网络层限流，只是就近的一道闸。
/// </summary>
public sealed class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> _state = new(StringComparer.Ordinal);

    public bool IsBlocked(string usernameLower)
        => _state.TryGetValue(usernameLower, out var entry)
           && entry.Count >= MaxFailures
           && entry.Until > DateTimeOffset.UtcNow;

    public void RecordFailure(string usernameLower)
        => _state.AddOrUpdate(
            usernameLower,
            _ => (1, DateTimeOffset.UtcNow.Add(Window)),
            (_, prev) => prev.Until > DateTimeOffset.UtcNow
                ? (prev.Count + 1, prev.Until)
                : (1, DateTimeOffset.UtcNow.Add(Window)));

    public void RecordSuccess(string usernameLower) => _state.TryRemove(usernameLower, out _);
}
