using Ingot.Contracts.Identity;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Identity;
using Ingot.Platform.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    PlatformUserResolver userResolver,
    LocalIdentityApplication store,
    LocalPasswordHasher hasher,
    ILogger<UsersController> logger) : PlatformApiController
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
            return InvalidRequest("username 不能为空。");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
            return InvalidRequest($"password 至少 {MinPasswordLength} 位。");
        if (!TryNormalizeRoles(request.Roles, out var roles, out var roleError))
            return InvalidRequest(roleError);
        if (!TryNormalizeSiteIds(request.SiteIds, out var siteIds, out var siteError))
            return InvalidRequest(siteError);

        var usernameLower = request.Username.Trim().ToLowerInvariant();
        if (await store.GetByUsernameAsync(usernameLower, ct).ConfigureAwait(false) is not null)
            return StateConflict("用户名已存在。");

        var now = DateTimeOffset.UtcNow;
        var created = await store.CreateAsync(new UserAccount
        {
            UserId = Guid.CreateVersion7(),
            Username = request.Username.Trim(),
            UsernameLower = usernameLower,
            DisplayName = request.DisplayName?.Trim() ?? string.Empty,
            PasswordHash = hasher.Hash(request.Password),
            Roles = roles,
            SiteIds = siteIds,
            CreatedAt = now,
            UpdatedAt = now
        }, ct).ConfigureAwait(false);
        Audit("user.created", created.UserId, $"roles={string.Join(',', roles)};sites={string.Join(',', siteIds)}");
        return Ok(ToSummary(created));
    }

    [HttpPost("{userId:guid}:set-roles")]
    public async Task<IActionResult> SetRoles(Guid userId, [FromBody] SetRolesRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (!TryNormalizeRoles(request?.Roles ?? [], out var roles, out var roleError))
            return InvalidRequest(roleError);
        var updated = await store.SetRolesAsync(userId, roles, ct).ConfigureAwait(false);
        if (updated) Audit("user.roles.updated", userId, $"roles={string.Join(',', roles)}");
        return updated ? NoContent() : ResourceNotFound();
    }

    [HttpPost("{userId:guid}:set-site-access")]
    public async Task<IActionResult> SetSiteAccess(
        Guid userId,
        [FromBody] SetSiteAccessRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (!TryNormalizeSiteIds(request?.SiteIds ?? [], out var siteIds, out var siteError))
            return InvalidRequest(siteError);
        var updated = await store.SetSiteAccessAsync(userId, siteIds, ct).ConfigureAwait(false);
        if (updated) Audit("user.site-access.updated", userId, $"sites={string.Join(',', siteIds)}");
        return updated ? NoContent() : ResourceNotFound();
    }

    [HttpPost("{userId:guid}:set-password")]
    public async Task<IActionResult> SetPassword(Guid userId, [FromBody] SetPasswordRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (string.IsNullOrWhiteSpace(request?.Password) || request.Password.Length < MinPasswordLength)
            return InvalidRequest($"password 至少 {MinPasswordLength} 位。");

        var updated = await store.SetPasswordHashAsync(userId, hasher.Hash(request.Password), ct).ConfigureAwait(false);
        if (updated) Audit("user.password.reset", userId, "sessions=revoked");
        return updated ? NoContent() : ResourceNotFound();
    }

    [HttpPost("{userId:guid}:set-disabled")]
    public async Task<IActionResult> SetDisabled(Guid userId, [FromBody] SetDisabledRequest? request, CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;

        if (request?.Disabled == true && string.Equals(userResolver.Resolve(User), userId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            return InvalidRequest("不能停用当前登录的账户。");
        var disabled = request?.Disabled ?? false;
        var updated = await store.SetDisabledAsync(userId, disabled, ct).ConfigureAwait(false);
        if (updated) Audit(disabled ? "user.disabled" : "user.enabled", userId, $"disabled={disabled}");
        return updated ? NoContent() : ResourceNotFound();
    }

    private IActionResult? DeniedAdmin()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ? null : AuthorizationDenied();
    }

    private void Audit(string action, Guid targetUserId, string details)
        => logger.LogInformation(
            "IdentityAudit action={Action} actorUserId={ActorUserId} targetUserId={TargetUserId} details={Details}",
            action,
            userResolver.Resolve(User) ?? "unknown",
            targetUserId,
            details);

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

    private static bool TryNormalizeSiteIds(
        IReadOnlyList<string> input,
        out IReadOnlyList<string> siteIds,
        out string error)
    {
        var normalized = input
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var invalid = normalized.FirstOrDefault(static value =>
            value.Length > 100 ||
            value == "*" ||
            value.Any(static character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'));
        if (invalid is not null)
        {
            siteIds = [];
            error = $"无效站点标识：{invalid}。";
            return false;
        }
        siteIds = normalized;
        error = string.Empty;
        return true;
    }

    private static UserSummary ToSummary(UserAccount user) => new()
    {
        UserId = user.UserId.ToString("D"),
        Username = user.Username,
        DisplayName = user.DisplayName,
        Roles = user.Roles,
        SiteIds = user.SiteIds,
        Disabled = user.Disabled,
        CreatedAt = user.CreatedAt
    };
}
