using System.Text.Json;
using Ingot.Agent;
using Ingot.Agent.Infrastructure;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class AgentArtifactTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ingot-artifacts-{Guid.NewGuid():N}");

    [Fact]
    public async Task Store_ShouldPersistArtifactsAndIsolateActors()
    {
        using var store = CreateStore();
        var saved = await store.SaveAsync(
            "engineer-a", "run-1", AgentArtifactKinds.ConnectorSpecification,
            "炉温连接器", "json", "{}", null);

        Assert.NotNull(await store.GetAsync("engineer-a", saved.ArtifactId));
        Assert.Null(await store.GetAsync("engineer-b", saved.ArtifactId));
        Assert.Single(await store.ListAsync("engineer-a", 10));
        Assert.Empty(await store.ListAsync("engineer-b", 10));
    }

    [Fact]
    public async Task Store_ClosesInterruptedRunsDuringStartup()
    {
        string runId;
        using (var first = CreateStore())
        {
            var run = new AgentRunSnapshot
            {
                RunId = Guid.CreateVersion7().ToString(),
                ActorId = "operator",
                Surface = ProductSurfaces.Agent,
                Purpose = RunPurposes.ConnectorCodeGeneration,
                Question = "interrupted",
                Mode = "standard",
                Status = AgentRunStatuses.Running,
                ModelProvider = "test",
                Model = "test",
                PromptVersion = "v1",
                ToolsetVersion = "v1",
                CreatedAt = DateTimeOffset.UtcNow,
                Usage = new AgentUsageSummary()
            };
            runId = run.RunId;
            await first.CreateAsync(run);
        }

        using var recoveredStore = CreateStore();
        await recoveredStore.InitializeAsync();
        var recovered = await recoveredStore.GetAsync(runId);
        Assert.Equal(AgentRunStatuses.Failed, recovered!.Status);
        Assert.NotNull(recovered.CompletedAt);
        Assert.Contains("服务重启", recovered.Error, StringComparison.Ordinal);
        var events = await recoveredStore.ReadEventsAsync(runId, 0, 10);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.RunFailed);
    }

    [Fact]
    public async Task ConnectorDraft_ShouldRefuseConnectionUntilConditionsAreComplete()
    {
        using var store = CreateStore();
        var tool = new DraftConnectorSpecificationTool(store);
        var result = await tool.ExecuteAsync(
            new AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?>
                {
                    ["name"] = "熔炉数据接入",
                    ["sourceCode"] = "FURNACE-01"
                }
            },
            new AgentExecutionContext
            {
                RunId = "run-1",
                ActorId = "engineer-a",
                Surface = ProductSurfaces.Agent,
                Purpose = RunPurposes.ConnectorCodeGeneration,
                Request = new CreateAgentRunRequest { Question = "接入熔炉" }
            });

        var artifact = Assert.Single(await store.ListAsync("engineer-a", 10));
        var specification = JsonSerializer.Deserialize<ConnectorSpecification>(artifact.Content,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(AgentToolAccess.ArtifactWrite, tool.Definition.Access);
        Assert.Equal(ConnectorSpecificationStatuses.NeedsInput, specification!.Status);
        Assert.Contains("protocol", specification.MissingConditions);
        Assert.Contains("Agent 不得尝试连接设备", result.Limitations[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectorDraft_ShouldMergeConversationAnswersIntoNextVersion()
    {
        using var store = CreateStore();
        var tool = new DraftConnectorSpecificationTool(store);
        var context = new AgentExecutionContext
        {
            RunId = "run-3",
            ActorId = "engineer-a",
            Surface = ProductSurfaces.Agent,
            Purpose = RunPurposes.ConnectorCodeGeneration,
            Request = new CreateAgentRunRequest { Question = "继续补齐连接器" }
        };
        await tool.ExecuteAsync(new AnalysisToolCall
        {
            Tool = tool.Definition.Name,
            Arguments = new Dictionary<string, string?>
            {
                ["name"] = "炉温连接器",
                ["sourceCode"] = "FURNACE-01"
            }
        }, context);
        var first = Assert.Single(await store.ListAsync("engineer-a", 10));

        await tool.ExecuteAsync(new AnalysisToolCall
        {
            Tool = tool.Definition.Name,
            Arguments = new Dictionary<string, string?>
            {
                ["artifactId"] = first.ArtifactId,
                ["protocol"] = "vendor-http-v2",
                ["endpoint"] = "https://furnace.local/events",
                ["dataContract"] = "temperature:double:C",
                ["samplingPolicy"] = "poll every 1s",
                ["successCriteria"] = "1000 fixtures without loss"
            }
        }, context);

        var versions = await store.ListAsync("engineer-a", 10);
        Assert.Equal(2, versions.Count);
        var current = JsonSerializer.Deserialize<ConnectorSpecification>(versions[0].Content,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(2, versions[0].Version);
        Assert.Equal(ConnectorSpecificationStatuses.ReadyForBuild, current!.Status);
        Assert.Empty(current.MissingConditions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private SqliteAgentStore CreateStore()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:DatabasePath"] = Path.Combine(_directory, "agent.db")
            })
            .Build();
        return new SqliteAgentStore(configuration);
    }

}
