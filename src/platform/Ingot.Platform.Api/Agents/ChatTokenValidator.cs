using System.Security.Cryptography;
using System.Text;
using Ingot.Agent;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Agents;

public sealed class ChatTokenValidator(IOptions<ChatOptions> options)
{
    private readonly ChatOptions _options = options.Value;

    public string CanonicalizeActorId(string actorId)
    {
        var normalized = actorId.Trim();
        var configured = _options.ActorTokens.Keys.FirstOrDefault(key =>
            string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase));
        return configured ?? normalized.ToLowerInvariant();
    }

    public bool IsAuthorized(string actorId, string? authorization)
    {
        if (!_options.RequireToken)
            return true;
        if (!_options.ActorTokens.TryGetValue(actorId, out var expected) ||
            string.IsNullOrWhiteSpace(expected) ||
            string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var actual = authorization["Bearer ".Length..].Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
