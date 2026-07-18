using System.Security.Cryptography;
using System.Text;
using Ingot.Central.Infrastructure.Events;
using Microsoft.Extensions.Options;

namespace Ingot.Central.Api.Events;

public sealed class EdgeTokenValidator(IOptions<CentralEventOptions> options)
{
    private readonly CentralEventOptions _options = options.Value;

    public bool IsAuthorized(string edgeId, string? authorization)
    {
        if (!_options.RequireToken)
            return true;

        if (!_options.EdgeTokens.TryGetValue(edgeId, out var expected) ||
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
