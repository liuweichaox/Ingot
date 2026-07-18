using Ingot.Agent;
using Ingot.Central.Api.Agents;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Central;

public sealed class ChatTokenValidatorTests
{
    [Fact]
    public void ChatCredentialsAreValidated()
    {
        var chat = new ChatTokenValidator(Options.Create(new ChatOptions
        {
            RequireToken = true,
            ActorTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["analyst"] = "chat-secret"
            }
        }));
        Assert.True(chat.IsAuthorized("analyst", "Bearer chat-secret"));
        Assert.False(chat.IsAuthorized("analyst", "Bearer wrong-secret"));
        Assert.False(chat.IsAuthorized("unknown", "Bearer chat-secret"));
    }
}
