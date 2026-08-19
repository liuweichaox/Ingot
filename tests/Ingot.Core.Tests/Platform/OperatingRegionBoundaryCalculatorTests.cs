using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class OperatingRegionBoundaryCalculatorTests
{
    private readonly OperatingRegionBoundaryCalculator _calculator = new();

    [Fact]
    public void CalculateMinMaxBoundary_WithValidRecords_ReturnsCorrectBounds()
    {
        var records = new[]
        {
            CreateValidationRecord("param1", ("temperature", 100m)),
            CreateValidationRecord("param1", ("temperature", 150m)),
            CreateValidationRecord("param1", ("temperature", 120m)),
        };

        var boundary = _calculator.CalculateMinMaxBoundary("temperature", records);

        Assert.Equal("temperature", boundary.ParameterName);
        Assert.Equal(100m, boundary.MinValue);
        Assert.Equal(150m, boundary.MaxValue);
        Assert.Equal(3, boundary.PointsUsed);
        Assert.True(boundary.CoverageConfidence > 0);
    }

    [Fact]
    public void CalculateMinMaxBoundary_WithNoRecords_ThrowsException()
    {
        var records = Array.Empty<ValidationHistoryRecord>();

        var ex = Assert.Throws<InvalidOperationException>(
            () => _calculator.CalculateMinMaxBoundary("temperature", records));

        Assert.Contains("没有验证记录", ex.Message);
    }

    [Fact]
    public void CalculateConvexHullBoundaries_WithSufficientData_ReturnsAllParameters()
    {
        var records = new[]
        {
            CreateValidationRecord("test", ("temp", 100m), ("pressure", 50m)),
            CreateValidationRecord("test", ("temp", 120m), ("pressure", 60m)),
            CreateValidationRecord("test", ("temp", 110m), ("pressure", 55m)),
        };

        var boundaries = _calculator.CalculateConvexHullBoundaries(
            records,
            new[] { "temp", "pressure" });

        Assert.Equal(2, boundaries.Count);
        Assert.Contains("temp", boundaries.Keys);
        Assert.Contains("pressure", boundaries.Keys);
        Assert.Equal(100m, boundaries["temp"].MinValue);
        Assert.Equal(120m, boundaries["temp"].MaxValue);
    }

    [Fact]
    public void IsPointWithinBoundary_WithValidPoint_ReturnsTrue()
    {
        var boundaries = new Dictionary<string, ConvexHullBoundary>
        {
            ["temperature"] = new("temperature", 100m, 150m, 5, 0.8m),
            ["pressure"] = new("pressure", 50m, 100m, 5, 0.8m)
        };
        var point = new Dictionary<string, decimal>
        {
            ["temperature"] = 125m,
            ["pressure"] = 75m
        };

        var isWithin = _calculator.IsPointWithinBoundary(boundaries, point, Array.Empty<ParameterConstraint>());

        Assert.True(isWithin);
    }

    [Fact]
    public void IsPointWithinBoundary_WithOutOfBoundsPoint_ReturnsFalse()
    {
        var boundaries = new Dictionary<string, ConvexHullBoundary>
        {
            ["temperature"] = new("temperature", 100m, 150m, 5, 0.8m)
        };
        var point = new Dictionary<string, decimal>
        {
            ["temperature"] = 160m // 超出上限
        };

        var isWithin = _calculator.IsPointWithinBoundary(boundaries, point, Array.Empty<ParameterConstraint>());

        Assert.False(isWithin);
    }

    [Fact]
    public void IsPointWithinBoundary_WithCoupledMinConstraint_RespectsConstraint()
    {
        var boundaries = new Dictionary<string, ConvexHullBoundary>
        {
            ["tempA"] = new("tempA", 100m, 150m, 5, 0.8m),
            ["tempB"] = new("tempB", 80m, 120m, 5, 0.8m)
        };
        var constraints = new[]
        {
            new ParameterConstraint(
                "c1",
                "region1",
                "COUPLED_MIN",
                "tempA",
                "tempB",
                "tempA >= tempB",
                "A 必须大于等于 B",
                DateTimeOffset.UtcNow)
        };

        // 满足约束：A > B
        var validPoint = new Dictionary<string, decimal>
        {
            ["tempA"] = 120m,
            ["tempB"] = 100m
        };
        Assert.True(_calculator.IsPointWithinBoundary(boundaries, validPoint, constraints));

        // 违反约束：A < B
        var invalidPoint = new Dictionary<string, decimal>
        {
            ["tempA"] = 90m,
            ["tempB"] = 100m
        };
        Assert.False(_calculator.IsPointWithinBoundary(boundaries, invalidPoint, constraints));
    }

    [Fact]
    public void IsPointWithinBoundary_WithMissingParameter_ReturnsFalse()
    {
        var boundaries = new Dictionary<string, ConvexHullBoundary>
        {
            ["temperature"] = new("temperature", 100m, 150m, 5, 0.8m),
            ["pressure"] = new("pressure", 50m, 100m, 5, 0.8m)
        };
        var point = new Dictionary<string, decimal>
        {
            ["temperature"] = 125m
            // 缺少 pressure
        };

        var isWithin = _calculator.IsPointWithinBoundary(boundaries, point, Array.Empty<ParameterConstraint>());

        Assert.False(isWithin);
    }

    private static ValidationHistoryRecord CreateValidationRecord(
        string experimentId,
        params (string, decimal)[] parameters)
    {
        var paramDict = parameters.ToDictionary(p => p.Item1, p => p.Item2);
        return new ValidationHistoryRecord(
            Guid.NewGuid().ToString(),
            "region-1",
            experimentId,
            "exec-1",
            DateTimeOffset.UtcNow,
            paramDict,
            "PASSED",
            95m,
            null,
            DateTimeOffset.UtcNow);
    }
}

public sealed class ExtensionRecommendationEngineTests
{
    private readonly ExtensionRecommendationEngine _engine = new();

    [Fact]
    public void RecommendExtension_WithValueBelowMin_ReturnsExtensionWithLowerBound()
    {
        var currentBoundary = new ConvexHullBoundary("temp", 100m, 150m, 10, 0.8m);

        var extension = _engine.RecommendExtension(
            "region-1",
            "exp-1",
            "temp",
            80m, // 低于 min
            currentBoundary);

        Assert.False(extension.ExtensionApproved);
        Assert.Equal(80m, extension.OutOfBoundsValue);
        Assert.Equal(100m, extension.OriginalMinValue);
        Assert.True(extension.ExtendedMinValue < 100m);
        Assert.Equal(150m, extension.ExtendedMaxValue); // 上限不变
    }

    [Fact]
    public void RecommendExtension_WithValueAboveMax_ReturnsExtensionWithUpperBound()
    {
        var currentBoundary = new ConvexHullBoundary("temp", 100m, 150m, 10, 0.8m);

        var extension = _engine.RecommendExtension(
            "region-1",
            "exp-1",
            "temp",
            170m, // 高于 max
            currentBoundary);

        Assert.False(extension.ExtensionApproved);
        Assert.Equal(170m, extension.OutOfBoundsValue);
        Assert.Equal(150m, extension.OriginalMaxValue);
        Assert.True(extension.ExtendedMaxValue > 150m);
        Assert.Equal(100m, extension.ExtendedMinValue); // 下限不变
    }

    [Fact]
    public void RecommendExtension_ExtensionMarginIsProportionalToRange()
    {
        // 宽范围：100-200
        var wideBoundary = new ConvexHullBoundary("param", 100m, 200m, 10, 0.8m);
        var wideExtension = _engine.RecommendExtension("r1", "e1", "param", 210m, wideBoundary);

        // 窄范围：100-110
        var narrowBoundary = new ConvexHullBoundary("param", 100m, 110m, 10, 0.8m);
        var narrowExtension = _engine.RecommendExtension("r1", "e1", "param", 115m, narrowBoundary);

        // 宽范围的扩展应该比窄范围更大（绝对值）
        var wideMargin = wideExtension.ExtendedMaxValue - wideExtension.OriginalMaxValue;
        var narrowMargin = narrowExtension.ExtendedMaxValue - narrowExtension.OriginalMaxValue;

        Assert.True(wideMargin > narrowMargin);
    }
}
