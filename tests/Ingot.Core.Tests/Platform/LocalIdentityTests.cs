// 验证平台组件 LocalIdentity 的成功、拒绝和安全边界。

using Ingot.Contracts.Identity;
using Ingot.Platform.Infrastructure.Identity;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class LocalIdentityTests
{
    [Fact]
    public void PasswordHash_VerifiesCorrectPassword_RejectsWrong()
    {
        var hasher = new LocalPasswordHasher();
        var hash = hasher.Hash("correct horse battery staple");
        Assert.NotEqual("correct horse battery staple", hash);        // 不落明文
        Assert.True(hasher.Verify(hash, "correct horse battery staple"));
        Assert.False(hasher.Verify(hash, "wrong password"));
    }

    [Fact]
    public void PasswordHash_IsSalted_TwoHashesDiffer()
    {
        var hasher = new LocalPasswordHasher();
        Assert.NotEqual(hasher.Hash("same"), hasher.Hash("same"));    // 加盐 → 每次不同
    }

    [Fact]
    public void TokenHash_IsDeterministic_AndTokensAreUrlSafeUnique()
    {
        var a = LocalPasswordHasher.NewToken();
        var b = LocalPasswordHasher.NewToken();
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
        Assert.Equal(LocalPasswordHasher.HashToken(a), LocalPasswordHasher.HashToken(a));
        Assert.NotEqual(LocalPasswordHasher.HashToken(a), LocalPasswordHasher.HashToken(b));
    }

    [Fact]
    public void LoginThrottle_BlocksAfterFiveFailures_ResetsOnSuccess()
    {
        var throttle = new LoginThrottle();
        Assert.False(throttle.IsBlocked("u"));
        for (var i = 0; i < 5; i++) throttle.RecordFailure("u");
        Assert.True(throttle.IsBlocked("u"));
        throttle.RecordSuccess("u");
        Assert.False(throttle.IsBlocked("u"));
    }

    [Fact]
    public void LoginThrottle_IsolatesUsers()
    {
        var throttle = new LoginThrottle();
        for (var i = 0; i < 5; i++) throttle.RecordFailure("victim");
        Assert.True(throttle.IsBlocked("victim"));
        Assert.False(throttle.IsBlocked("other"));
    }

    [Fact]
    public void LoginThrottle_BoundsRotatingIdentityStateAndFailsClosed()
    {
        var throttle = new LoginThrottle();
        for (var index = 0; index < LoginThrottle.MaxTrackedIdentities + 100; index++)
            throttle.RecordFailure($"rotating-{index}");

        Assert.Equal(LoginThrottle.MaxTrackedIdentities, throttle.TrackedIdentityCount);
        Assert.True(throttle.IsBlocked("previously-unseen"));
    }

    [Fact]
    public void LoginThrottle_BlocksPasswordSprayByClientKey()
    {
        var throttle = new LoginThrottle();
        for (var index = 0; index < 25; index++)
            throttle.RecordFailure($"user-{index}", "192.0.2.10");

        Assert.True(throttle.IsBlocked("new-user", "192.0.2.10"));
        Assert.False(throttle.IsBlocked("new-user", "192.0.2.11"));
    }

    [Theory]
    [InlineData("platform.admin", true)]
    [InlineData("process.engineer", true)]
    [InlineData("quality.inspector", true)]
    [InlineData("superuser", false)]
    [InlineData("", false)]
    public void PlatformRoleNames_IsKnown(string role, bool known)
        => Assert.Equal(known, PlatformRoleNames.IsKnown(role));
}
