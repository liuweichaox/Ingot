// 暴露认证配置与受限本地登录入口，不接受客户端伪造授权范围。
using System.Security.Claims;
using Ingot.Contracts.Identity;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Identity;
using Ingot.Platform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    LocalIdentityApplication store,
    LocalPasswordHasher hasher,
    LoginThrottle throttle,
    IOptions<LocalAuthOptions> options,
    IConfiguration configuration) : PlatformApiController
{
    private const int MaxUsernameLength = 128;
    private const int MaxPasswordLength = 1024;

    private static readonly string TimingEqualizerHash = new LocalPasswordHasher().Hash("timing-equalizer");

    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult Configuration()
    {
        var configuredMode = configuration["Authentication:Mode"] ?? "Local";
        var mode = configuredMode.Equals("Oidc", StringComparison.OrdinalIgnoreCase)
            ? "oidc"
            : configuredMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)
                ? "disabled"
                : "local";
        return Ok(new AuthConfigurationResponse
        {
            Mode = mode,
            Authority = mode == "oidc" ? configuration["Authentication:Authority"]?.TrimEnd('/') : null,
            ClientId = mode == "oidc" ? configuration["Authentication:Oidc:ClientId"] : null,
            Scope = configuration["Authentication:Oidc:Scope"] ?? "openid profile"
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return InvalidRequest("用户名和口令不能为空。");
        if (request.Username.Length > MaxUsernameLength || request.Password.Length > MaxPasswordLength)
            return InvalidRequest("用户名或口令超过允许长度。");

        var usernameLower = request.Username.Trim().ToLowerInvariant();
        var clientKey = ResolveClientKey();
        if (throttle.IsBlocked(usernameLower, clientKey))
            return RateLimited("登录尝试过于频繁，请稍后再试。");

        var user = await store.GetByUsernameAsync(usernameLower, ct).ConfigureAwait(false);

        var passwordMatches = hasher.Verify(user?.PasswordHash ?? TimingEqualizerHash, request.Password);
        var verified = user is { Disabled: false } && passwordMatches;
        if (!verified)
        {
            throttle.RecordFailure(usernameLower, clientKey);
            return AuthenticationRequired("用户名或口令错误。");
        }
        throttle.RecordSuccess(usernameLower);

        var token = LocalPasswordHasher.NewToken();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(options.Value.SessionLifetimeHours, 1, 720));
        await store.CreateSessionAsync(LocalPasswordHasher.HashToken(token), user!.UserId, expiresAt, ct).ConfigureAwait(false);

        return Ok(new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.UserId.ToString("D"),
            Username = user.Username,
            DisplayName = user.DisplayName,
            Roles = user.Roles,
            SiteIds = user.SiteIds
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            if (token.Length > 0)
                await store.RevokeSessionAsync(LocalPasswordHasher.HashToken(token), ct).ConfigureAwait(false);
        }
        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return AuthenticationRequired();
        var roleClaimType = (User.Identity as ClaimsIdentity)?.RoleClaimType ?? ClaimTypes.Role;
        return Ok(new IdentityResponse
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                     User.FindFirstValue("sub") ??
                     string.Empty,
            Username = User.Identity.Name ?? string.Empty,
            Roles = User.FindAll(roleClaimType).Select(static claim => claim.Value).ToArray(),
            SiteIds = User.FindAll(PlatformClaimTypes.SiteId).Select(static claim => claim.Value).ToArray()
        });
    }

    private string ResolveClientKey()
    {
        var proxyAddress = Request.Headers["X-Real-IP"].ToString().Trim();
        if (System.Net.IPAddress.TryParse(proxyAddress, out var parsedProxyAddress))
            return parsedProxyAddress.ToString();
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
