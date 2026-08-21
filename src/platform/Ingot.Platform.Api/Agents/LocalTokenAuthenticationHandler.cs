using System.Security.Claims;
using System.Text.Encodings.Web;
using Ingot.Platform.Application.Identity;
using Ingot.Platform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Agents;

public sealed class LocalTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ILocalUserStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IngotLocal";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0)
            return AuthenticateResult.NoResult();

        var session = await store.ValidateSessionAsync(LocalPasswordHasher.HashToken(token), Context.RequestAborted)
            .ConfigureAwait(false);
        if (session is null)
            return AuthenticateResult.Fail("会话无效或已过期。");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString("D")),
            new(ClaimTypes.Name, session.Username)
        };
        claims.AddRange(session.Roles.Select(static role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(session.SiteIds.Select(static siteId => new Claim(PlatformClaimTypes.SiteId, siteId)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
