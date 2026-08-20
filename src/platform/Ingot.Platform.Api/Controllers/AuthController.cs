using System.Security.Claims;
using Ingot.Platform.Api.Agents;
using Ingot.Contracts.Identity;
using Ingot.Platform.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Controllers;

/// <summary>本地账户登录 / 注销 / 当前身份。OIDC 模式下无本地用户，登录恒返回 401。</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    ILocalUserStore store,
    LocalPasswordHasher hasher,
    LoginThrottle throttle,
    IOptions<LocalAuthOptions> options) : PlatformApiController
{
    // 用户不存在时也执行一次等价的 PBKDF2 校验，消除"账户是否存在"的时序旁路。进程内计算一次。
    private static readonly string TimingEqualizerHash = new LocalPasswordHasher().Hash("timing-equalizer");

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return InvalidRequest("用户名和口令不能为空。");

        var usernameLower = request.Username.Trim().ToLowerInvariant();
        if (throttle.IsBlocked(usernameLower))
            return RateLimited("登录尝试过于频繁，请稍后再试。");

        var user = await store.GetByUsernameAsync(usernameLower, ct).ConfigureAwait(false);
        // 无论用户是否存在都执行一次哈希校验（不存在时用等时哈希），返回同一错误，避免用户名枚举与时序差异。
        var passwordMatches = hasher.Verify(user?.PasswordHash ?? TimingEqualizerHash, request.Password);
        var verified = user is { Disabled: false } && passwordMatches;
        if (!verified)
        {
            throttle.RecordFailure(usernameLower);
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
        return Ok(new IdentityResponse
        {
            UserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            Username = User.Identity.Name ?? string.Empty,
            Roles = User.FindAll(ClaimTypes.Role).Select(static claim => claim.Value).ToArray(),
            SiteIds = User.FindAll(PlatformClaimTypes.SiteId).Select(static claim => claim.Value).ToArray()
        });
    }
}
