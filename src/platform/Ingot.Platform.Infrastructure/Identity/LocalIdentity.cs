using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Ingot.Platform.Infrastructure.Identity;

public sealed class LocalAuthOptions
{
    public int SessionLifetimeHours { get; set; } = 12;

    public string? SeedAdminUsername { get; set; }
    public string? SeedAdminPassword { get; set; }
}

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
