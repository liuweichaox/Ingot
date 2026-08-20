namespace Ingot.Edge.Application.Abstractions;

public static class LogAudiences
{
    public const string Operator = "operator";
    public const string System = "system";
}

/// <summary>把框架日志与现场人员可操作的领域事件分开。</summary>
public static class LogAudienceClassifier
{
    public static (string Audience, string Category) Classify(string? level, string? source)
    {
        var normalizedLevel = level?.Trim().ToLowerInvariant();
        if (normalizedLevel is "warning" or "error" or "fatal")
            return (LogAudiences.Operator, CategoryFor(source));

        if (ContainsAny(source, "Acquisition", "Protocol", "Device"))
            return (LogAudiences.Operator, "设备采集");
        if (ContainsAny(source, "Configuration", "Deployment"))
            return (LogAudiences.Operator, "配置应用");
        if (ContainsAny(source, "Outbox", "Shipment", "Delivery"))
            return (LogAudiences.Operator, "数据上行");

        return (LogAudiences.System, "系统运行");
    }

    private static string CategoryFor(string? source)
    {
        if (ContainsAny(source, "Acquisition", "Protocol", "Device")) return "设备采集";
        if (ContainsAny(source, "Configuration", "Deployment")) return "配置应用";
        if (ContainsAny(source, "Outbox", "Shipment", "Delivery", "Reporting")) return "数据上行";
        return "节点服务";
    }

    private static bool ContainsAny(string? source, params string[] values)
        => !string.IsNullOrWhiteSpace(source)
           && values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
}
