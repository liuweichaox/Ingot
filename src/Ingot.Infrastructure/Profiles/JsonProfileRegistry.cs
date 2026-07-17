using System.Text.Json;
using System.Text.Json.Serialization;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Ingot.Domain.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Infrastructure.Profiles;

/// <summary>
///     从 JSON 目录加载行业 Profile。Profile 在进程启动时固定，避免运行期间领域语言漂移。
/// </summary>
public sealed class JsonProfileRegistry : IProfileRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly IReadOnlyDictionary<string, ProfileDefinition> _profiles;

    public JsonProfileRegistry(
        IOptions<ProfileOptions> options,
        ILogger<JsonProfileRegistry> logger)
    {
        var directory = ResolveDirectory(options.Value.Directory);
        _profiles = Load(directory, logger);
    }

    public IReadOnlyCollection<ProfileDefinition> Profiles => _profiles.Values.ToArray();

    public bool TryGet(string name, out ProfileDefinition profile)
        => _profiles.TryGetValue(name, out profile!);

    public IReadOnlyList<string> Validate(DeviceConfig config)
    {
        if (config.SchemaVersion < 2)
            return [];

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(config.Profile))
        {
            errors.Add("SchemaVersion=2 时 Profile 不能为空");
            return errors;
        }

        if (!TryGet(config.Profile, out var profile))
        {
            errors.Add($"未找到 Profile: {config.Profile}");
            return errors;
        }

        ValidateObjectRef(config.Asset, "Asset", profile, errors);
        for (var index = 0; index < config.EventRules.Count; index++)
        {
            var rule = config.EventRules[index];
            var prefix = $"EventRule {index + 1}";
            var subject = rule.Subject ?? config.Asset;
            ValidateObjectRef(subject, $"{prefix}.Subject", profile, errors);
            if (rule.Trigger.Kind == EventTriggerKind.ValueChanged)
            {
                ValidateEventType(rule.GetEventType(), rule.ContextKeys, prefix, profile, errors);
            }
            else
            {
                ValidateEventType(rule.GetStartedEventType(), rule.ContextKeys, prefix, profile, errors);
                ValidateEventType(rule.GetCompletedEventType(), rule.ContextKeys, prefix, profile, errors);
            }
        }

        return errors;
    }

    private static void ValidateObjectRef(
        ObjectRef? subject,
        string path,
        ProfileDefinition profile,
        List<string> errors)
    {
        if (subject is null)
            return;

        if (!profile.ObjectTypes.Contains(subject.Type, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{path} 使用了 Profile '{profile.Name}' 未声明的对象类型: {subject.Type}");
    }

    private static void ValidateEventType(
        string eventType,
        IReadOnlyCollection<string> contextKeys,
        string path,
        ProfileDefinition profile,
        List<string> errors)
    {
        var definition = profile.EventTypes.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, eventType, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            errors.Add($"{path} 使用了 Profile '{profile.Name}' 未声明的事件类型: {eventType}");
            return;
        }

        foreach (var requiredKey in definition.RequiredContext)
        {
            if (!contextKeys.Contains(requiredKey, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{path} 的事件 {eventType} 缺少 Profile 必需上下文: {requiredKey}");
        }
    }

    private static IReadOnlyDictionary<string, ProfileDefinition> Load(
        string directory,
        ILogger logger)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Profile 目录不存在: {directory}");

        var profiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<ProfileDefinition>(json, JsonOptions)
                          ?? throw new InvalidDataException($"Profile 文件为空或无法解析: {file}");
            ValidateDefinition(profile, file);

            if (!profiles.TryAdd(profile.Name, profile))
                throw new InvalidDataException($"存在重复 Profile 名称 '{profile.Name}': {file}");

            logger.LogInformation(
                "已加载 Profile {ProfileName}: ObjectTypes={ObjectTypeCount}, EventTypes={EventTypeCount}",
                profile.Name,
                profile.ObjectTypes.Count,
                profile.EventTypes.Count);
        }

        if (profiles.Count == 0)
            throw new InvalidDataException($"Profile 目录中没有 JSON 定义: {directory}");

        return profiles;
    }

    private static void ValidateDefinition(ProfileDefinition profile, string file)
    {
        if (profile.SchemaVersion != 1)
            throw new InvalidDataException($"{file}: 当前仅支持 Profile SchemaVersion=1");
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidDataException($"{file}: Profile.Name 不能为空");
        if (profile.ObjectTypes.Count == 0)
            throw new InvalidDataException($"{file}: Profile.ObjectTypes 不能为空");
        if (profile.EventTypes.Count == 0)
            throw new InvalidDataException($"{file}: Profile.EventTypes 不能为空");
        if (profile.ObjectTypes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.ObjectTypes.Count)
            throw new InvalidDataException($"{file}: Profile.ObjectTypes 存在重复项");
        if (profile.EventTypes.Select(static item => item.Type)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != profile.EventTypes.Count)
            throw new InvalidDataException($"{file}: Profile.EventTypes 存在重复项");
    }

    private static string ResolveDirectory(string configuredDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(configuredDirectory) ? "Profiles" : configuredDirectory.Trim();
        return Path.IsPathRooted(directory)
            ? directory
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, directory));
    }
}
