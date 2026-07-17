using Ingot.Domain.Events;
using Ingot.Infrastructure.Events;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class EventRuleEvaluatorTests
{
    private static readonly EventRule Rule = new()
    {
        RuleId = "cycle",
        Trigger = new EventRuleTrigger
        {
            Kind = EventTriggerKind.EdgePair,
            Tag = "D6006",
            DataType = "short"
        }
    };

    [Theory]
    [InlineData(0, 1, true, false)]
    [InlineData(1, 0, false, true)]
    [InlineData(0, 0, false, false)]
    [InlineData(1, 2, false, false)]
    public void EvaluateEdgePair_ShouldPreserveConditionalAcquisitionSemantics(
        short previous,
        short current,
        bool shouldStart,
        bool shouldComplete)
    {
        var result = EventRuleEvaluator.Evaluate(Rule, previous, current);

        Assert.Equal(shouldStart, result.ShouldStart);
        Assert.Equal(shouldComplete, result.ShouldComplete);
    }

    [Fact]
    public void EvaluateValueChanged_ShouldEmitOnlyWhenValueChanges()
    {
        var rule = new EventRule
        {
            Trigger = new EventRuleTrigger { Kind = EventTriggerKind.ValueChanged }
        };

        Assert.False(EventRuleEvaluator.Evaluate(rule, "LOT-01", "LOT-01").ShouldEmit);
        Assert.True(EventRuleEvaluator.Evaluate(rule, "LOT-01", "LOT-02").ShouldEmit);
    }

    [Fact]
    public void EvaluateBitFlag_ShouldEmitRaisedAndClearedPair()
    {
        var rule = new EventRule
        {
            Trigger = new EventRuleTrigger { Kind = EventTriggerKind.BitFlag, Bit = 2 }
        };

        Assert.True(EventRuleEvaluator.Evaluate(rule, 0, 4).ShouldStart);
        Assert.True(EventRuleEvaluator.Evaluate(rule, 4, 0).ShouldComplete);
        Assert.False(EventRuleEvaluator.Evaluate(rule, 4, 12).ShouldStart);
    }

    [Fact]
    public void EvaluateThreshold_ShouldDetectEnteringAndLeavingRange()
    {
        var rule = new EventRule
        {
            Trigger = new EventRuleTrigger
            {
                Kind = EventTriggerKind.Threshold,
                Threshold = 10,
                ThresholdDirection = ThresholdDirection.AboveOrEqual
            }
        };

        Assert.True(EventRuleEvaluator.Evaluate(rule, 9, 10).ShouldStart);
        Assert.True(EventRuleEvaluator.Evaluate(rule, 11, 8).ShouldComplete);
    }
}
