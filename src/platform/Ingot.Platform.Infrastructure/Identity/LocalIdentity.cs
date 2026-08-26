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
    private const int MaxUserFailures = 5;
    private const int MaxClientFailures = 25;
    internal const int MaxTrackedIdentities = 4096;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset Until)> _state =
        new(StringComparer.Ordinal);
    private DateTimeOffset _nextPruneAt;

    internal int TrackedIdentityCount
    {
        get
        {
            lock (_gate)
                return _state.Count;
        }
    }

    public bool IsBlocked(string usernameLower, string? clientKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            MaybePruneExpired(now);
            return IsBlockedKey(UserKey(usernameLower), MaxUserFailures, now) ||
                   (!string.IsNullOrWhiteSpace(clientKey) &&
                    IsBlockedKey(ClientKey(clientKey), MaxClientFailures, now));
        }
    }

    public void RecordFailure(string usernameLower, string? clientKey = null)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            MaybePruneExpired(now);
            RecordFailureKey(UserKey(usernameLower), now);
            if (!string.IsNullOrWhiteSpace(clientKey))
                RecordFailureKey(ClientKey(clientKey), now);
        }
    }

    public void RecordSuccess(string usernameLower)
    {
        lock (_gate)
            _state.Remove(UserKey(usernameLower));
    }

    private bool IsBlockedKey(string key, int maxFailures, DateTimeOffset now)
    {
        if (_state.TryGetValue(key, out var entry))
            return entry.Count >= maxFailures && entry.Until > now;
        // Once the bounded table is full, fail closed for unseen identities instead of
        // allowing username rotation to create unbounded memory and password-hash work.
        return _state.Count >= MaxTrackedIdentities;
    }

    private void RecordFailureKey(string key, DateTimeOffset now)
    {
        if (_state.TryGetValue(key, out var previous))
        {
            _state[key] = previous.Until > now
                ? (previous.Count + 1, previous.Until)
                : (1, now.Add(Window));
            return;
        }
        if (_state.Count < MaxTrackedIdentities)
            _state[key] = (1, now.Add(Window));
    }

    private void MaybePruneExpired(DateTimeOffset now)
    {
        if (now < _nextPruneAt)
            return;
        foreach (var key in _state
                     .Where(pair => pair.Value.Until <= now)
                     .Select(static pair => pair.Key)
                     .ToArray())
            _state.Remove(key);
        _nextPruneAt = now.AddSeconds(30);
    }

    private static string UserKey(string value) => $"user:{value}";
    private static string ClientKey(string value) => $"client:{value}";
}
