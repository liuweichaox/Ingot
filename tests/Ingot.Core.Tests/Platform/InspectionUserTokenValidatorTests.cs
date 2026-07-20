using Ingot.Platform.Api.Inspections;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class InspectionUserTokenValidatorTests
{
    [Fact]
    public void IsAuthorized_ShouldBindTokenToSubmittedUser()
    {
        var validator = CreateValidator(requireToken: true);

        Assert.True(validator.RequiresToken);
        Assert.True(validator.IsAuthorized("OPERATOR-001", "Bearer operator-secret"));
        Assert.False(validator.IsAuthorized("OPERATOR-002", "Bearer operator-secret"));
        Assert.False(validator.IsAuthorized("OPERATOR-001", "Bearer wrong"));
    }

    [Fact]
    public void IsAuthorized_ShouldAllowButNotVerifyWhenDisabled()
    {
        var validator = CreateValidator(requireToken: false);

        Assert.False(validator.RequiresToken);
        Assert.True(validator.IsAuthorized("UNREGISTERED", null));
    }

    private static InspectionUserTokenValidator CreateValidator(bool requireToken)
        => new(Options.Create(new InspectionSubmissionOptions
        {
            RequireToken = requireToken,
            UserTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OPERATOR-001"] = "operator-secret"
            }
        }));
}
