using Ingot.Domain.Profiles;

namespace Ingot.Application.Abstractions;

/// <summary>
///     提供已加载的行业 Profile，并验证源配置中的领域引用。
/// </summary>
public interface IProfileRegistry
{
    IReadOnlyCollection<ProfileDefinition> Profiles { get; }

    bool TryGet(string name, out ProfileDefinition profile);

    IReadOnlyList<string> Validate(DeviceConfig config);
}
