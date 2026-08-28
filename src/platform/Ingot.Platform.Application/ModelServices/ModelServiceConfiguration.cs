// 定义模型服务页面配置的应用契约、持久化端口和用例入口。
namespace Ingot.Platform.Application.ModelServices;

public sealed record ModelServiceConfigurationView
{
    public bool Enabled { get; init; }

    public string Provider { get; init; } = "Deterministic";

    public string Protocol { get; init; } = "Responses";

    public string? BaseUrl { get; init; }

    public string FastModel { get; init; } = "deterministic-v1";

    public string ReasoningModel { get; init; } = "deterministic-v1";

    public bool HasApiKey { get; init; }

    public string? ApiKeyHint { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? UpdatedBy { get; init; }

    public string Source { get; init; } = "deployment";
}

public sealed record SaveModelServiceConfigurationCommand
{
    public bool Enabled { get; init; }

    public required string Provider { get; init; }

    public required string Protocol { get; init; }

    public string? BaseUrl { get; init; }

    public required string FastModel { get; init; }

    public required string ReasoningModel { get; init; }

    public string? ApiKey { get; init; }

    public bool ClearApiKey { get; init; }
}

/// <summary>持久化并读取当前模型服务配置，读取结果不包含明文 API key。</summary>
public interface IModelServiceConfigurationStore
{
    ModelServiceConfigurationView GetCurrent();

    Task<ModelServiceConfigurationView> SaveAsync(
        SaveModelServiceConfigurationCommand command,
        string actorUserId,
        CancellationToken ct = default);
}

/// <summary>封装模型服务配置的应用用例，避免 API 直接依赖持久化端口。</summary>
public sealed class ModelServiceConfigurationApplication(IModelServiceConfigurationStore store)
{
    public ModelServiceConfigurationView GetCurrent() => store.GetCurrent();

    public Task<ModelServiceConfigurationView> SaveAsync(
        SaveModelServiceConfigurationCommand command,
        string actorUserId,
        CancellationToken ct = default)
        => store.SaveAsync(command, actorUserId, ct);
}
