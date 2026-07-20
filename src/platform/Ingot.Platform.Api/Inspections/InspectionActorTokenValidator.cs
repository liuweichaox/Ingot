using System.Security.Cryptography;
using System.Text;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Inspections;

public sealed class InspectionActorTokenValidator(IOptions<InspectionSubmissionOptions> options)
{
    private readonly InspectionSubmissionOptions _options = options.Value;

    public bool RequiresToken => _options.RequireToken;

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

