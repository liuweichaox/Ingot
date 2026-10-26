// 验证模型服务配置的管理员边界、只写密钥和拒绝路径。
using System.Security.Claims;
using System.Text.Json;
using Ingot.Agent;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.ModelServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ModelServiceConfigurationControllerTests
{
    [Fact]
    public void Get_ReturnsSanitizedViewWithoutApiKey()
    {
        var store = new MemoryStore(new ModelServiceConfigurationView
        {
            Enabled = true,
            Provider = "ExampleProvider",
            Protocol = "Responses",
            BaseUrl = "https://models.example.com",
            FastModel = "fast",
            ReasoningModel = "reasoning",
            HasApiKey = true,
            ApiKeyHint = "••••cdef",
            Source = "platform"
        });
        var controller = Controller(store, PlatformRoles.PlatformAdministrator);

        var result = Assert.IsType<OkObjectResult>(controller.Get());
        var json = JsonSerializer.Serialize(result.Value);

        Assert.DoesNotContain("secret-abcdef", json, StringComparison.Ordinal);
        Assert.Contains("apiKeyHint", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_DeniesNonAdministrator()
    {
        var controller = Controller(new MemoryStore(new ModelServiceConfigurationView()), PlatformRoles.ProcessEngineer);

        var result = Assert.IsAssignableFrom<ObjectResult>(controller.Get());

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Save_RequiresApiKeyBeforeEnabling()
    {
        var controller = Controller(new MemoryStore(new ModelServiceConfigurationView()), PlatformRoles.PlatformAdministrator);

        var result = await controller.Save(Command() with { ApiKey = null }, CancellationToken.None);

        var error = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
    }

    [Fact]
    public async Task Save_AcceptsWriteOnlyApiKeyAndReturnsHint()
    {
        var store = new MemoryStore(new ModelServiceConfigurationView());
        var controller = Controller(store, PlatformRoles.PlatformAdministrator);

        var result = await controller.Save(Command(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var view = Assert.IsType<ModelServiceConfigurationView>(ok.Value);
        Assert.Equal("secret-abcdef", store.LastCommand?.ApiKey);
        Assert.True(view.HasApiKey);
        Assert.Equal("••••cdef", view.ApiKeyHint);
    }

    [Fact]
    public async Task Test_UsesTypedApiKeyWithoutSavingIt()
    {
        var store = new MemoryStore(new ModelServiceConfigurationView { HasApiKey = true });
        var tester = new StubConnectionTester();
        var controller = Controller(
            store,
            PlatformRoles.PlatformAdministrator,
            new ModelServiceConnectionSettings { ApiKey = "old-secret", Revision = "current" },
            tester);

        var result = await controller.Test(Command() with { ApiKey = "new-secret" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("new-secret", tester.LastSettings?.ApiKey);
        Assert.Null(store.LastCommand);
    }

    private static SaveModelServiceConfigurationCommand Command() => new()
    {
        Enabled = true,
        Provider = "ExampleProvider",
        Protocol = "Responses",
        BaseUrl = "https://models.example.com",
        FastModel = "fast",
        ReasoningModel = "reasoning",
        ApiKey = "secret-abcdef"
    };

    private static ModelServiceConfigurationController Controller(
        MemoryStore store,
        string role,
        ModelServiceConnectionSettings? currentSettings = null,
        StubConnectionTester? tester = null)
        => new(
            new PlatformUserResolver(new ProductionEnvironment()),
            new ModelServiceConfigurationApplication(store),
            new MemoryConfigurationProvider(currentSettings ?? new ModelServiceConnectionSettings()),
            tester ?? new StubConnectionTester(),
            NullLogger<ModelServiceConfigurationController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "administrator"),
                        new Claim(ClaimTypes.Role, role)
                    ], "test"))
                }
            }
        };

    private sealed class MemoryStore(ModelServiceConfigurationView current) : IModelServiceConfigurationStore
    {
        public SaveModelServiceConfigurationCommand? LastCommand { get; private set; }

        public ModelServiceConfigurationView GetCurrent() => current;

        public Task<ModelServiceConfigurationView> SaveAsync(
            SaveModelServiceConfigurationCommand command,
            string actorUserId,
            CancellationToken ct = default)
        {
            LastCommand = command;
            return Task.FromResult(current with
            {
                Enabled = command.Enabled,
                Provider = command.Provider,
                Protocol = command.Protocol,
                BaseUrl = command.BaseUrl,
                FastModel = command.FastModel,
                ReasoningModel = command.ReasoningModel,
                HasApiKey = !string.IsNullOrWhiteSpace(command.ApiKey),
                ApiKeyHint = string.IsNullOrWhiteSpace(command.ApiKey) ? null : "••••cdef",
                Source = "platform"
            });
        }
    }

    private sealed class MemoryConfigurationProvider(ModelServiceConnectionSettings current) : IModelServiceConfigurationProvider
    {
        public ModelServiceConnectionSettings Current { get; } = current;
    }

    private sealed class StubConnectionTester : IModelServiceConnectionTester
    {
        public ModelServiceConnectionSettings? LastSettings { get; private set; }

        public Task<ModelServiceConnectionTestResult> TestAsync(
            ModelServiceConnectionSettings settings,
            CancellationToken ct = default)
        {
            LastSettings = settings;
            return Task.FromResult(new ModelServiceConnectionTestResult(true, "连接测试通过。"));
        }
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
