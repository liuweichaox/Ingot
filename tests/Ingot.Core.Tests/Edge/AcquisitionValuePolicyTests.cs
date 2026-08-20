// 验证边缘组件 AcquisitionValuePolicy 的协议、状态和失败边界。

using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionValuePolicyTests
{
    [Fact]
    public void RejectsValueWhoseQualityIsNotAccepted()
    {
        var mapping = Mapping() with
        {
            QualityPath = "quality",
            AcceptedQualityValues = ["Good"]
        };

        var error = Assert.Throws<InvalidDataException>(() =>
            AcquisitionValuePolicy.Resolve(
                new Dictionary<string, object?> { ["value"] = 12, ["quality"] = "Bad" },
                mapping,
                "double"));

        Assert.Contains("不在允许范围", error.Message);
    }

    [Fact]
    public void OptionalMissingValueDoesNotRequireQualityField()
    {
        var mapping = Mapping() with
        {
            Required = false,
            QualityPath = "quality",
            AcceptedQualityValues = ["Good"]
        };

        Assert.Null(AcquisitionValuePolicy.Resolve(
            new Dictionary<string, object?>(), mapping, "double"));
    }

    [Fact]
    public void AppliesDefaultAndClampPolicies()
    {
        var fallback = Mapping() with
        {
            MissingValueBehavior = "use-default",
            DefaultValue = "1"
        };
        Assert.True(Assert.IsType<bool>(AcquisitionValuePolicy.Resolve(
            new Dictionary<string, object?>(), fallback, "boolean")));

        var bounded = Mapping() with
        {
            Minimum = 0,
            Maximum = 100,
            OutOfRangeBehavior = "clamp"
        };
        Assert.Equal(100d, AcquisitionValuePolicy.Resolve(
            new Dictionary<string, object?> { ["value"] = 120 }, bounded, "double"));
    }

    [Fact]
    public void IntegerTargetRejectsFractionalScaledValue()
    {
        var mapping = Mapping() with { Scale = 0.1 };
        var error = Assert.Throws<InvalidDataException>(() =>
            AcquisitionValuePolicy.Resolve(
                new Dictionary<string, object?> { ["value"] = 125 }, mapping, "integer"));
        Assert.Contains("不是整数", error.Message);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void NumericTargetsRejectNonFiniteDeviceValues(string value)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            AcquisitionValuePolicy.Resolve(
                new Dictionary<string, object?> { ["value"] = value }, Mapping(), "double"));

        Assert.Contains("不是有限数字", error.Message);
    }

    private static AcquisitionValueMapping Mapping() => new()
    {
        DataItemCode = "value",
        SourcePath = "value"
    };
}
