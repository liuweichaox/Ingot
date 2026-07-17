using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Acquisition;

namespace Ingot.Infrastructure.Events;

/// <summary>
///     无状态规则判定，支持边沿对、值变化、位标志和阈值触发器。
/// </summary>
public static class EventRuleEvaluator
{
    public static EventRuleEvaluation Evaluate(EventRule rule, object? previous, object? current)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Trigger.Kind switch
        {
            EventTriggerKind.EdgePair => new EventRuleEvaluation(
                ShouldTrigger(rule.Trigger.StartTriggerMode, previous, current),
                ShouldTrigger(rule.Trigger.EndTriggerMode, previous, current),
                false),
            EventTriggerKind.ValueChanged => new EventRuleEvaluation(
                false,
                false,
                !ValuesEqual(previous, current)),
            EventTriggerKind.BitFlag => EvaluateBitFlag(rule.Trigger.Bit, previous, current),
            EventTriggerKind.Threshold => EvaluateThreshold(rule.Trigger, previous, current),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Trigger.Kind, "未知事件触发器类型。")
        };
    }

    public static bool IsPairActive(EventRule rule, object? value)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (value is null)
            return false;

        return rule.Trigger.Kind switch
        {
            EventTriggerKind.EdgePair => rule.Trigger.StartTriggerMode == AcquisitionTrigger.RisingEdge
                ? PlcValueAccessor.IsTriggerActive(value)
                : !PlcValueAccessor.IsTriggerActive(value),
            EventTriggerKind.BitFlag => (Convert.ToUInt64(value) & (1UL << rule.Trigger.Bit)) != 0,
            EventTriggerKind.Threshold => IsInsideThreshold(Convert.ToDecimal(value), rule.Trigger),
            EventTriggerKind.ValueChanged => false,
            _ => false
        };
    }

    private static EventRuleEvaluation EvaluateBitFlag(int bit, object? previous, object? current)
    {
        if (previous is null || current is null)
            return default;
        if (bit is < 0 or > 63)
            throw new ArgumentOutOfRangeException(nameof(bit), "Bit 必须在 0 到 63 之间。");

        var mask = 1UL << bit;
        var wasSet = (Convert.ToUInt64(previous) & mask) != 0;
        var isSet = (Convert.ToUInt64(current) & mask) != 0;
        return new EventRuleEvaluation(!wasSet && isSet, wasSet && !isSet, false);
    }

    private static EventRuleEvaluation EvaluateThreshold(
        EventRuleTrigger trigger,
        object? previous,
        object? current)
    {
        if (previous is null || current is null)
            return default;

        var wasInside = IsInsideThreshold(Convert.ToDecimal(previous), trigger);
        var isInside = IsInsideThreshold(Convert.ToDecimal(current), trigger);
        return new EventRuleEvaluation(!wasInside && isInside, wasInside && !isInside, false);
    }

    private static bool IsInsideThreshold(decimal value, EventRuleTrigger trigger)
        => trigger.ThresholdDirection switch
        {
            ThresholdDirection.AboveOrEqual => value >= trigger.Threshold,
            ThresholdDirection.BelowOrEqual => value <= trigger.Threshold,
            _ => false
        };

    private static bool ValuesEqual(object? previous, object? current)
    {
        if (previous is null || current is null)
            return previous is null && current is null;

        if (previous is string || current is string)
            return string.Equals(previous.ToString(), current.ToString(), StringComparison.Ordinal);

        try
        {
            return Convert.ToDecimal(previous) == Convert.ToDecimal(current);
        }
        catch (Exception) when (previous is not IConvertible || current is not IConvertible)
        {
            return Equals(previous, current);
        }
    }

    private static bool ShouldTrigger(
        AcquisitionTrigger mode,
        object? previous,
        object? current)
    {
        if (previous is null || current is null)
            return false;

        var wasActive = PlcValueAccessor.IsTriggerActive(previous);
        var isActive = PlcValueAccessor.IsTriggerActive(current);

        return mode switch
        {
            AcquisitionTrigger.RisingEdge => !wasActive && isActive,
            AcquisitionTrigger.FallingEdge => wasActive && !isActive,
            _ => false
        };
    }
}

public readonly record struct EventRuleEvaluation(bool ShouldStart, bool ShouldComplete, bool ShouldEmit);
