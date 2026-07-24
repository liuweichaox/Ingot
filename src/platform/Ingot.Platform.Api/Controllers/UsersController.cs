using Ingot.Platform.Api.Agents;
using Ingot.Contracts.Identity;
using Ingot.Platform.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

/// <summary>本地账户管理：仅平台管理员。OIDC 模式下账户由外部管理，这些端点无实际用户可管。</summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    PlatformUserResolver userResolver,
    ILocalUserStore store,
    LocalPasswordHasher hasher) : ControllerBase
{
    private const int MinPasswordLength = 8;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedAdmin() ?? Ok(new { data = (await store.ListAsync(ct).ConfigureAwait(false)).Select(ToSummary) });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (request is null || string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { error = "username 不能为空。" });
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
            return BadRequest(new { error = $"password 至少 {MinPasswordLength} 位。" });
        if (!TryNormalizeRoles(request.Roles, out var roles, out var roleError))
            return BadRequest(new { error = roleError });

        var usernameLower = request.Username.Trim().ToLowerInvariant();
        if (await store.GetByUsernameAsync(usernameLower, ct).ConfigureAwait(false) is not null)
            return Conflict(new { error = "用户名已存在。" });

        var now = DateTimeOffset.UtcNow;
        var created = await store.CreateAsync(new UserAccount
        {
            UserId = Guid.CreateVersion7(),
            Username = request.Username.Trim(),
            UsernameLower = usernameLower,
            DisplayName = request.DisplayName?.Trim() ?? string.Empty,
            PasswordHash = hasher.Hash(request.Password),
            Roles = roles,
            CreatedAt = now,
            UpdatedAt = now
        }, ct).ConfigureAwait(false);
        return Ok(ToSummary(created));
    }

    [HttpPost("{userId:guid}:set-roles")]
    public async Task<IActionResult> SetRoles(Guid userId, [FromBody] SetRolesRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (!TryNormalizeRoles(request?.Roles ?? [], out var roles, out var roleError))
            return BadRequest(new { error = roleError });
        return await store.SetRolesAsync(userId, roles, ct).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    [HttpPost("{userId:guid}:set-password")]
    public async Task<IActionResult> SetPassword(Guid userId, [FromBody] SetPasswordRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (string.IsNullOrWhiteSpace(request?.Password) || request.Password.Length < MinPasswordLength)
            return BadRequest(new { error = $"password 至少 {MinPasswordLength} 位。" });
        // 改密会注销该用户其它会话（见 store 实现）。
        return await store.SetPasswordHashAsync(userId, hasher.Hash(request.Password), ct).ConfigureAwait(false)
            ? NoContent() : NotFound();
    }

    [HttpPost("{userId:guid}:set-disabled")]
    public async Task<IActionResult> SetDisabled(Guid userId, [FromBody] SetDisabledRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        // 不允许停用自己，避免管理员把自己锁在门外。
        if (request?.Disabled == true && string.Equals(userResolver.Resolve(User), userId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "不能停用当前登录的账户。" });
        return await store.SetDisabledAsync(userId, request?.Disabled ?? false, ct).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    private IActionResult? DeniedAdmin()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        return identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ? null : Forbid();
    }

    private static bool TryNormalizeRoles(
        IReadOnlyList<string> input, out IReadOnlyList<string> roles, out string error)
    {
        var normalized = input
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Select(static role => role.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknown = normalized.FirstOrDefault(static role => !PlatformRoleNames.IsKnown(role));
        if (unknown is not null)
        {
            roles = [];
            error = $"未知角色：{unknown}。允许：{string.Join(", ", PlatformRoleNames.All)}。";
            return false;
        }
        roles = normalized;
        error = string.Empty;
        return true;
    }

    private static UserSummary ToSummary(UserAccount user) => new()
    {
        UserId = user.UserId.ToString("D"),
        Username = user.Username,
        DisplayName = user.DisplayName,
        Roles = user.Roles,
        Disabled = user.Disabled,
        CreatedAt = user.CreatedAt
    };
}
