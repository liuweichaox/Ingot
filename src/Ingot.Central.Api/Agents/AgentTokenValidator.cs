using System.Security.Cryptography;
using System.Text;
using Ingot.Agent;
using Microsoft.Extensions.Options;

namespace Ingot.Central.Api.Agents;

public sealed class AgentTokenValidator(IOptions<AgentOptions> options)
{
    public const string DesktopClientId = "ingot-agent-desktop";

    private readonly AgentOptions _options = options.Value;

    public bool RequiresToken => _options.RequireToken;

    public static bool IsDesktopClient(string? client)
        => string.Equals(client?.Trim(), DesktopClientId, StringComparison.Ordinal);

    public string CanonicalizeActorId(string actorId)
    {
        var normalized = actorId.Trim();
        var configured = _options.ActorTokens.Keys.FirstOrDefault(key =>
            string.Equals(key, normalized, StringComparison.OrdinalIgnoreCase));
        return configured ?? normalized.ToLowerInvariant();
    }

    public bool CanApprovePackaging(string actorId)
    {
        var canonical = CanonicalizeActorId(actorId);
        return _options.PackagingApprovers.Any(approver =>
            string.Equals(approver?.Trim(), canonical, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsAuthorized(string actorId, string? authorization)
    {
        if (!_options.RequireToken)
            return true;
        if (!_options.ActorTokens.TryGetValue(actorId, out var expected) ||
            string.IsNullOrWhiteSpace(expected) ||
            string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var actual = authorization["Bearer ".Length..].Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
