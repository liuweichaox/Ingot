using System;
using System.Collections.Generic;
using System.Linq;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;

namespace Ingot.Infrastructure.DeviceConfigs;

internal static class DeviceConfigValidator
{
    public static ConfigValidationResult Validate(DeviceConfig config)
    {
        var errors = new List<string>();

        if (config.SchemaVersion is not (1 or 2))
            errors.Add($"当前支持 SchemaVersion=1 或 2，实际为 {config.SchemaVersion}");

        if (string.IsNullOrWhiteSpace(config.SourceCode))
            errors.Add("SourceCode（v1 可使用 PlcCode）不能为空");

        if (config.SchemaVersion == 2 && string.IsNullOrWhiteSpace(config.Adapter))
            errors.Add("SchemaVersion=2 时 Adapter 不能为空");

        if (!string.Equals(config.Adapter, "plc", StringComparison.OrdinalIgnoreCase))
            errors.Add($"当前仅实现 Adapter=plc，实际为 {config.Adapter}");

        if (string.IsNullOrWhiteSpace(config.Driver))
            errors.Add("Driver 不能为空");

        if (string.IsNullOrWhiteSpace(config.Host))
            errors.Add("主机地址不能为空");
        else if (!IsValidHost(config.Host))
            errors.Add($"无效的主机地址: {config.Host}");

        if (config.Port == 0)
            errors.Add("端口不能为0");

        if (string.IsNullOrWhiteSpace(config.HeartbeatMonitorRegister))
            errors.Add("心跳检测地址不能为空");

        if (config.HeartbeatPollingInterval <= 0)
            errors.Add("心跳检测间隔必须大于 0");

        if (config.Channels.Count == 0 && config.EventRules.Count == 0)
        {
            errors.Add("至少需要配置一个采集通道或 EventRule");
        }

        if (config.Channels.Count > 0)
        {
            var duplicateChannels = config.Channels
                .Where(static channel =>
                    !string.IsNullOrWhiteSpace(channel.ChannelCode) && !string.IsNullOrWhiteSpace(channel.Measurement))
                .GroupBy(static channel => $"{channel.ChannelCode}|{channel.Measurement}", StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToArray();

            foreach (var duplicateChannel in duplicateChannels)
            {
                var parts = duplicateChannel.Split('|', 2);
                errors.Add($"存在重复的通道定义: ChannelCode={parts[0]}, Measurement={parts[1]}");
            }

            for (var i = 0; i < config.Channels.Count; i++)
                ValidateChannel(config.Channels[i], i + 1, errors);
        }

        ValidateEventRules(config, errors);

        return new ConfigValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private static void ValidateEventRules(DeviceConfig config, List<string> errors)
    {
        var duplicateRuleIds = config.EventRules
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.RuleId))
            .GroupBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);
        foreach (var ruleId in duplicateRuleIds)
            errors.Add($"存在重复的 EventRule.RuleId: {ruleId}");

        for (var index = 0; index < config.EventRules.Count; index++)
        {
            var rule = config.EventRules[index];
            var prefix = $"EventRule {index + 1}";

            if (string.IsNullOrWhiteSpace(rule.RuleId))
                errors.Add($"{prefix} 的 RuleId 不能为空");
            if (string.IsNullOrWhiteSpace(rule.Category))
                errors.Add($"{prefix} 的 Category 不能为空");
            if (rule.Subject is null && config.Asset is null)
                errors.Add($"{prefix} 必须配置 Subject，或在源配置上配置默认 Asset");
            if (string.IsNullOrWhiteSpace(rule.Trigger.Tag))
                errors.Add($"{prefix} 的 Trigger.Tag 不能为空");
            if (string.IsNullOrWhiteSpace(rule.Trigger.DataType))
                errors.Add($"{prefix} 的 Trigger.DataType 不能为空");
            if (string.Equals(rule.Trigger.DataType, "string", StringComparison.OrdinalIgnoreCase) &&
                rule.Trigger.StringByteLength <= 0)
                errors.Add($"{prefix} 的字符串触发器必须配置 StringByteLength");
            if (rule.Trigger.Kind == EventTriggerKind.BitFlag && rule.Trigger.Bit is < 0 or > 63)
                errors.Add($"{prefix} 的 Trigger.Bit 必须在 0 到 63 之间");
            if (rule.Trigger.Kind == EventTriggerKind.ValueChanged &&
                string.IsNullOrWhiteSpace(rule.GetEventType()))
                errors.Add($"{prefix} 的 EventType 不能为空");
            if (rule.SetContext.Keys.Any(static key => string.IsNullOrWhiteSpace(key)))
                errors.Add($"{prefix} 的 SetContext 不能包含空键");

            ValidateSnapshotFields(rule.SnapshotOnStart, $"{prefix}.SnapshotOnStart", errors);
            ValidateSnapshotFields(rule.SnapshotOnEnd, $"{prefix}.SnapshotOnEnd", errors);
        }
    }

    private static void ValidateSnapshotFields(
        IReadOnlyCollection<EventSnapshotField> fields,
        string path,
        List<string> errors)
    {
        var duplicates = fields
            .Where(static field => !string.IsNullOrWhiteSpace(field.FieldName))
            .GroupBy(static field => field.FieldName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);
        foreach (var duplicate in duplicates)
            errors.Add($"{path} 存在重复 FieldName: {duplicate}");

        var index = 0;
        foreach (var field in fields)
        {
            index++;
            var prefix = $"{path}[{index}]";
            if (string.IsNullOrWhiteSpace(field.FieldName))
                errors.Add($"{prefix}.FieldName 不能为空");
            if (string.IsNullOrWhiteSpace(field.Tag))
                errors.Add($"{prefix}.Tag 不能为空");
            if (string.IsNullOrWhiteSpace(field.DataType))
                errors.Add($"{prefix}.DataType 不能为空");
            if (string.Equals(field.DataType, "string", StringComparison.OrdinalIgnoreCase) &&
                field.StringByteLength <= 0)
                errors.Add($"{prefix} 的字符串字段必须配置 StringByteLength");
        }
    }

    private static bool IsValidHost(string host)
    {
        var hostNameType = Uri.CheckHostName(host.Trim());
        return hostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }

    private static void ValidateChannel(AcquisitionChannel channel, int index, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(channel.ChannelCode))
            errors.Add($"通道 {index} 的 ChannelCode 不能为空");

        if (string.IsNullOrWhiteSpace(channel.Measurement))
            errors.Add($"通道 {index} 的 Measurement 不能为空");

        if (channel.BatchSize <= 0)
            errors.Add($"通道 {index} 的 BatchSize 必须大于 0");

        if (channel.AcquisitionInterval < 0)
            errors.Add($"通道 {index} 的 AcquisitionInterval 不能小于 0");

        if (channel.EnableBatchRead)
        {
            if (string.IsNullOrWhiteSpace(channel.BatchReadRegister))
                errors.Add($"通道 {index} 启用批量读取时必须配置 BatchReadRegister");

            if (channel.BatchReadLength == 0)
                errors.Add($"通道 {index} 启用批量读取时 BatchReadLength 必须大于 0");
        }

        if (channel.AcquisitionMode == AcquisitionMode.Conditional)
            ValidateConditionalAcquisition(channel, index, errors);

        if (channel.AcquisitionMode == AcquisitionMode.Always && channel.Metrics is not { Count: > 0 })
            errors.Add($"通道 {index} 在 Always 模式下至少需要一个 Metric");

        if (channel.Metrics is { Count: > 0 })
            ValidateMetrics(channel, index, errors);
    }

    private static void ValidateConditionalAcquisition(AcquisitionChannel channel, int index, List<string> errors)
    {
        if (channel.ConditionalAcquisition == null)
        {
            errors.Add($"通道 {index} 在 Conditional 模式下必须配置 ConditionalAcquisition");
            return;
        }

        if (string.IsNullOrWhiteSpace(channel.ConditionalAcquisition.Register))
            errors.Add($"通道 {index} 的 ConditionalAcquisition.Register 不能为空");

        if (string.IsNullOrWhiteSpace(channel.ConditionalAcquisition.DataType))
            errors.Add($"通道 {index} 的 ConditionalAcquisition.DataType 不能为空");

        if (channel.ConditionalAcquisition.StartTriggerMode == null)
            errors.Add($"通道 {index} 的 ConditionalAcquisition.StartTriggerMode 不能为空");

        if (channel.ConditionalAcquisition.EndTriggerMode == null)
            errors.Add($"通道 {index} 的 ConditionalAcquisition.EndTriggerMode 不能为空");
    }

    private static void ValidateMetrics(AcquisitionChannel channel, int index, List<string> errors)
    {
        var duplicateFields = channel.Metrics!
            .Where(static metric => !string.IsNullOrWhiteSpace(metric.FieldName))
            .GroupBy(static metric => metric.FieldName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        foreach (var duplicateField in duplicateFields)
            errors.Add($"通道 {index} 存在重复的 FieldName: {duplicateField}");

        for (var metricIndex = 0; metricIndex < channel.Metrics!.Count; metricIndex++)
        {
            var metric = channel.Metrics[metricIndex];
            var metricPrefix = $"通道 {index} 的 Metric {metricIndex + 1}";

            if (string.IsNullOrWhiteSpace(metric.FieldName))
                errors.Add($"{metricPrefix} 的 FieldName 不能为空");

            if (string.IsNullOrWhiteSpace(metric.Register))
                errors.Add($"{metricPrefix} 的 Register 不能为空");

            if (string.IsNullOrWhiteSpace(metric.DataType))
                errors.Add($"{metricPrefix} 的 DataType 不能为空");

            if (string.Equals(metric.DataType, "string", StringComparison.OrdinalIgnoreCase) &&
                metric.StringByteLength <= 0)
                errors.Add($"{metricPrefix} 的 StringByteLength 必须大于 0");
        }
    }
}
