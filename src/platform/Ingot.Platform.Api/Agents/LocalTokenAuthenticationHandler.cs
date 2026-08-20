using System.Security.Claims;
using System.Text.Encodings.Web;
using Ingot.Platform.Infrastructure.Identity;
using Ingot.Platform.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Agents;

/// <summary>
///     本地会话令牌认证：读取 Authorization: Bearer &lt;token&gt;，按其 SHA-256 查会话，
///     解析出用户与角色声明。生产自托管默认模式，消除对外部 OIDC 的强制依赖。
/// </summary>
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
            return AuthenticateResult.NoResult(); // 匿名：受保护端点由 fallback 策略拒绝

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
