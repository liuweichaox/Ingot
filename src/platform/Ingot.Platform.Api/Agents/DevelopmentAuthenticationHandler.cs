using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Agents;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IngotDevelopment";
    public const string UserHeaderName = "X-Ingot-Development-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var requestedUser = Request.Headers[UserHeaderName].FirstOrDefault()?.Trim();
        var userId = IsSafeDevelopmentUser(requestedUser) ? requestedUser! : "operator";
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new(ClaimTypes.Role, PlatformRoles.QualityInspector),
            new(ClaimTypes.Role, PlatformRoles.QualityReviewer),
            new(ClaimTypes.Role, PlatformRoles.ProcessEngineer),
            new(ClaimTypes.Role, PlatformRoles.PlatformAdministrator),
            new(PlatformClaimTypes.SiteId, "*")
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }

    private static bool IsSafeDevelopmentUser(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 64 &&
           value.All(static character =>
               char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
}
