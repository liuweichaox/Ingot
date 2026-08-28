// 验证模型服务配置的管理员边界、只写密钥和拒绝路径。
using System.Security.Claims;
using System.Text.Json;
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

    private static ModelServiceConfigurationController Controller(MemoryStore store, string role)
        => new(
            new PlatformUserResolver(new ProductionEnvironment()),
            new ModelServiceConfigurationApplication(store),
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

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
