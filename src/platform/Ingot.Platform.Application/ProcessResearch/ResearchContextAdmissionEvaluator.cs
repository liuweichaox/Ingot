using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed record ResearchContextAdmissionResult(
    bool Admitted,
    IReadOnlyList<string> ExclusionReasons);

/// <summary>
///     对研究观察的运行上下文执行确定性准入。场景包只提供版本化策略；
///     本服务不推断字段别名，也不会用记录存在代替明确的上下文捕获状态。
/// </summary>
public sealed class ResearchContextAdmissionEvaluator
{
    public const string ScenarioPackageContextKey = "scenario_package";
    public const string PolicyHashContextKey = "scenario_context_policy_hash";
    public const string ObservationPolicyHashContextKey = "research_context_policy_hash";
    public const string ObservationScenarioContextKey = "research_scenario_package";
    public const string CaptureStatusContextKey = "context_capture_status";

    public ResearchContextAdmissionResult Evaluate(
        IReadOnlyDictionary<string, string> context,
        ScenarioPackage? scenarioPackage)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reasons = new List<string>();
        var captureStatus = Value(context, CaptureStatusContextKey)?.ToLowerInvariant();

        if (captureStatus == "configuration_missing")
            reasons.Add("生产上下文未解析（context_capture_status=configuration_missing）");
        else if (scenarioPackage is not null && string.IsNullOrWhiteSpace(captureStatus))
            reasons.Add("缺少生产上下文捕获状态（context_capture_status）");
        else if (!string.IsNullOrWhiteSpace(captureStatus) &&
                 captureStatus is not ("resolved" or "source_provided"))
            reasons.Add($"生产上下文捕获状态无效（{captureStatus}）");

        if (scenarioPackage is not null)
        {
            foreach (var field in scenarioPackage.ContextFields
                         .Where(static field => field.Mode == ScenarioContextModes.RequiredForAnalysis)
                         .OrderBy(static field => field.FieldCode, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(Value(context, field.FieldCode)))
                    reasons.Add($"缺少分析必需上下文：{field.FieldCode}");
            }
        }

        return new ResearchContextAdmissionResult(reasons.Count == 0, reasons);
    }

    public static bool TryParseScenarioPackageReference(
        IReadOnlyDictionary<string, string> projectContext,
        out string packageId,
        out int version)
    {
        packageId = "";
        version = 0;
        var reference = Value(projectContext, ScenarioPackageContextKey);
        if (string.IsNullOrWhiteSpace(reference))
            return false;
        var separator = reference.LastIndexOf(':');
        if (separator <= 0 || separator == reference.Length - 1 ||
            !int.TryParse(reference[(separator + 1)..], out version) || version < 1)
            throw new ProcessResearchRuleException(
                "研发项目的工艺配置引用必须使用 <package-id>:<version> 格式。");
        packageId = reference[..separator].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ProcessResearchRuleException("研发项目的工艺配置标识不能为空。");
        return true;
    }

    public static string ComputePolicyHash(ScenarioPackage scenarioPackage)
    {
        ArgumentNullException.ThrowIfNull(scenarioPackage);
        var payload = new
        {
            packageId = scenarioPackage.PackageId.Trim().ToLowerInvariant(),
            scenarioPackage.Version,
            contextFields = scenarioPackage.ContextFields
                .OrderBy(static field => field.FieldCode, StringComparer.Ordinal)
                .Select(static field => new
                {
                    fieldCode = field.FieldCode.Trim().ToLowerInvariant(),
                    mode = field.Mode.Trim().ToLowerInvariant(),
                    field.MinimumCoverage,
                    field.MinimumFactorOverlap
                })
        };
        return Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }

    private static string? Value(IReadOnlyDictionary<string, string> context, string key)
    {
        if (context.TryGetValue(key, out var exact))
            return exact?.Trim();
        var pair = context.FirstOrDefault(item =>
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        return pair.Value?.Trim();
    }
}
