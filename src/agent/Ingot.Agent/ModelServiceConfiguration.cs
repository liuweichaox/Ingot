using Microsoft.Extensions.Options;

namespace Ingot.Agent;

/// <summary>模型服务当前生效的连接信息；API key 只在服务端内存中流转。</summary>
public sealed record ModelServiceConnectionSettings
{
    public bool Enabled { get; init; }

    public string Provider { get; init; } = "Deterministic";

    public string Protocol { get; init; } = "Responses";

    public string? BaseUrl { get; init; }

    public string FastModel { get; init; } = "deterministic-v1";

    public string ReasoningModel { get; init; } = "deterministic-v1";

    public string? ApiKey { get; init; }

    public string Revision { get; init; } = "deployment";
}

/// <summary>向 Agent 提供已经解析的当前模型服务配置。</summary>
public interface IModelServiceConfigurationProvider
{
    ModelServiceConnectionSettings Current { get; }

    Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>在未配置平台存储时提供无凭据的部署默认值。</summary>
public sealed class DeploymentModelServiceConfigurationProvider : IModelServiceConfigurationProvider
{
    public DeploymentModelServiceConfigurationProvider(IOptions<ChatOptions> options)
    {
        var value = options.Value;
        Current = new ModelServiceConnectionSettings
        {
            Enabled = value.Enabled,
            Provider = value.Provider,
            Protocol = value.Protocol,
            BaseUrl = value.BaseUrl,
            FastModel = value.FastModel,
            ReasoningModel = value.ReasoningModel,
            ApiKey = null,
            Revision = "deployment"
        };
    }

    public ModelServiceConnectionSettings Current { get; }
}
