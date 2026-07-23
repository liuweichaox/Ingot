using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Agents;

/// <summary>
/// Local development uses the platform identity directly and never introduces
/// a second product login. Production always uses the configured JWT issuer.
/// </summary>
public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IngotDevelopment";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "operator"),
            new(ClaimTypes.Name, "operator"),
            new(ClaimTypes.Role, PlatformRoles.QualityInspector),
            new(ClaimTypes.Role, PlatformRoles.QualityReviewer),
            new(ClaimTypes.Role, PlatformRoles.ProcessEngineer),
            new(ClaimTypes.Role, PlatformRoles.PlatformAdministrator)
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
