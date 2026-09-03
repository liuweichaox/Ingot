// 定义工艺配置变更与删除操作的显式结果类型。
namespace Ingot.Platform.Application.ProcessConfiguration;

public enum ProcessConfigurationMutationStatus
{
    Applied,
    StateConflict,
    Referenced,
    NotFound
}

public sealed record ProcessConfigurationMutationResult<T>
{
    public required ProcessConfigurationMutationStatus Status { get; init; }

    public T? Value { get; init; }

    public T? Existing { get; init; }

    public bool Succeeded => Status == ProcessConfigurationMutationStatus.Applied;

    public static ProcessConfigurationMutationResult<T> Applied(T value) => new()
    {
        Status = ProcessConfigurationMutationStatus.Applied,
        Value = value
    };

    public static ProcessConfigurationMutationResult<T> StateConflict(T? existing) => new()
    {
        Status = ProcessConfigurationMutationStatus.StateConflict,
        Existing = existing
    };
}

public sealed record ProcessConfigurationDeleteResult
{
    public required ProcessConfigurationMutationStatus Status { get; init; }

    public string? ExistingStatus { get; init; }

    public bool Succeeded => Status == ProcessConfigurationMutationStatus.Applied;

    public static ProcessConfigurationDeleteResult Applied() => new()
    {
        Status = ProcessConfigurationMutationStatus.Applied
    };

    public static ProcessConfigurationDeleteResult NotFound() => new()
    {
        Status = ProcessConfigurationMutationStatus.NotFound
    };

    public static ProcessConfigurationDeleteResult StateConflict(string? existingStatus) => new()
    {
        Status = ProcessConfigurationMutationStatus.StateConflict,
        ExistingStatus = existingStatus
    };

    public static ProcessConfigurationDeleteResult Referenced() => new()
    {
        Status = ProcessConfigurationMutationStatus.Referenced
    };
}
