using Ingot.Agent;
using Ingot.Central.Api.Agents;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Central;

public sealed class ChatTokenValidatorTests
{
    [Fact]
    public void ChatAndDesktopCredentialsAreIndependent()
    {
        var chat = new ChatTokenValidator(Options.Create(new ChatOptions
        {
            RequireToken = true,
            ActorTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["analyst"] = "chat-secret"
            }
        }));
        var agent = new AgentTokenValidator(Options.Create(new AgentOptions
        {
            RequireToken = true,
            ActorTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["engineer"] = "agent-secret"
            }
        }));

        Assert.True(chat.IsAuthorized("analyst", "Bearer chat-secret"));
        Assert.False(chat.IsAuthorized("analyst", "Bearer agent-secret"));
        Assert.True(agent.IsAuthorized("engineer", "Bearer agent-secret"));
        Assert.False(agent.IsAuthorized("engineer", "Bearer chat-secret"));
    }

    [Theory]
    [InlineData("ingot-agent-desktop", true)]
    [InlineData("INGOT-AGENT-DESKTOP", false)]
    [InlineData("browser", false)]
    [InlineData(null, false)]
    public void DesktopClientHeader_IsExact(string? value, bool expected)
        => Assert.Equal(expected, AgentTokenValidator.IsDesktopClient(value));
}
