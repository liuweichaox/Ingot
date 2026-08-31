using System.Net;
using System.Text;
using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MechanismClaimDraftGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_RejectsWhenModelServiceIsNotEnabled()
    {
        var generator = new OpenAiCompatibleMechanismClaimDraftGenerator(
            new NoHttpClientFactory(),
            new FixedModelServiceConfigurationProvider(new ModelServiceConnectionSettings
            {
                Enabled = false,
                BaseUrl = "https://models.example.com/v1",
                ApiKey = "not-used"
            }));

        var error = await Assert.ThrowsAsync<ResearchAssetRuleException>(() => generator.GenerateAsync(
            new MechanismClaimDraftGenerationContext
            {
                ProjectName = "Test project",
                SourceTitle = "Test source",
                SourceHash = "hash"
            }));

        Assert.Equal("模型服务未在系统管理页面启用。", error.Message);
    }

    [Fact]
    public async Task GenerateAsync_UsesPageConfiguredResponsesServiceAndSkipsReasoningOutput()
    {
        var handler = new RecordingHandler("""
            {"output":[{"type":"reasoning","summary":[]},{"type":"message","content":[{"type":"output_text","text":"{\"name\":\"Draft\",\"mechanismType\":\"thermal\",\"statement\":\"Statement\",\"falsificationCondition\":\"Condition\"}"}]}]}
            """);
        var generator = Generator(handler, "Responses");

        var draft = await generator.GenerateAsync(Context());

        Assert.Equal("Draft", draft.Name);
        Assert.Equal("reasoning-model", draft.GeneratorModel);
        Assert.Equal("/v1/responses", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("page-api-key", handler.AuthorizationParameter);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("json_object", body.RootElement.GetProperty("text").GetProperty("format")
            .GetProperty("type").GetString());
        Assert.Equal("reasoning-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task GenerateAsync_UsesPageConfiguredChatCompletionsService()
    {
        var handler = new RecordingHandler("""
            {"choices":[{"message":{"content":"{\"name\":\"Draft\",\"mechanismType\":\"thermal\",\"statement\":\"Statement\",\"falsificationCondition\":\"Condition\"}"}}]}
            """);
        var generator = Generator(handler, "ChatCompletions");

        var draft = await generator.GenerateAsync(Context());

        Assert.Equal("Draft", draft.Name);
        Assert.Equal("/v1/chat/completions", handler.RequestUri?.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("reasoning-model", body.RootElement.GetProperty("model").GetString());
    }

    private static OpenAiCompatibleMechanismClaimDraftGenerator Generator(
        RecordingHandler handler,
        string protocol)
        => new(
            new SingleClientFactory(new HttpClient(handler)),
            new FixedModelServiceConfigurationProvider(new ModelServiceConnectionSettings
            {
                Enabled = true,
                Protocol = protocol,
                BaseUrl = "https://models.example.com/v1",
                ReasoningModel = "reasoning-model",
                ApiKey = "page-api-key"
            }));

    private static MechanismClaimDraftGenerationContext Context() => new()
    {
        ProjectName = "Test project",
        SourceTitle = "Test source",
        SourceHash = "hash"
    };

    private sealed class FixedModelServiceConfigurationProvider(ModelServiceConnectionSettings settings)
        : IModelServiceConfigurationProvider
    {
        public ModelServiceConnectionSettings Current { get; } = settings;
    }

    private sealed class NoHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("HTTP must not be called.");
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
