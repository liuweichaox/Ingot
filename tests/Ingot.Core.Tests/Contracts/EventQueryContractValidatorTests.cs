using Ingot.Contracts.Events;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class EventQueryContractValidatorTests
{
    [Fact]
    public void TryValidate_ShouldAcceptValidSharedQueryBoundary()
    {
        Assert.True(EventQueryContractValidator.TryValidate(
            DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-17T01:00:00Z"),
            0,
            500,
            new Dictionary<string, string> { ["material_lot"] = "LOT-001" },
            out _));
    }

    [Theory]
    [InlineData(-1, 100, "游标")]
    [InlineData(0, 0, "limit")]
    [InlineData(0, 501, "limit")]
    public void TryValidate_ShouldRejectInvalidCursorOrLimit(
        long cursor,
        int limit,
        string expectedError)
    {
        Assert.False(EventQueryContractValidator.TryValidate(
            null,
            null,
            cursor,
            limit,
            new Dictionary<string, string>(),
            out var error));
        Assert.Contains(expectedError, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRejectReverseRangeAndInvalidContextKey()
    {
        var later = DateTimeOffset.Parse("2026-07-17T01:00:00Z");
        var earlier = DateTimeOffset.Parse("2026-07-17T00:00:00Z");
        Assert.False(EventQueryContractValidator.TryValidate(
            later,
            earlier,
            null,
            100,
            new Dictionary<string, string>(),
            out var rangeError));
        Assert.Contains("from", rangeError, StringComparison.Ordinal);

        Assert.False(EventQueryContractValidator.TryValidate(
            null,
            null,
            null,
            100,
            new Dictionary<string, string> { [""] = "value" },
            out var contextError));
        Assert.Contains("ctx.<key>", contextError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("0", true, "0")]
    [InlineData("42", true, "42")]
    [InlineData("-1", false, null)]
    [InlineData("not-a-number", false, null)]
    public void TryParseCursor_ShouldBeStrict(
        string? input,
        bool expectedSuccess,
        string? expectedCursor)
    {
        Assert.Equal(
            expectedSuccess,
            EventQueryContractValidator.TryParseCursor(input, out var cursor));
        Assert.Equal(
            expectedCursor is null ? null : long.Parse(expectedCursor),
            cursor);
    }
}
