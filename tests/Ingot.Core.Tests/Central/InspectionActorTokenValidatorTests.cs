using Ingot.Central.Api.Inspections;
using Ingot.Central.Infrastructure.Inspections;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Central;

public sealed class InspectionActorTokenValidatorTests
{
    [Fact]
    public void IsAuthorized_ShouldBindTokenToSubmittedActor()
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

    private static InspectionActorTokenValidator CreateValidator(bool requireToken)
        => new(Options.Create(new InspectionSubmissionOptions
        {
            RequireToken = requireToken,
            ActorTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OPERATOR-001"] = "operator-secret"
            }
        }));
}
