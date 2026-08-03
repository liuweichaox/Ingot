using Ingot.Domain.Events;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionSampleMetadataTests
{
    [Fact]
    public void SameSourceSequenceIsEmittedOnlyOnce()
    {
        var deduplicator = new AcquisitionSourceDeduplicator();
        var first = CreateSample(42L);
        var retry = first with { EventId = Guid.CreateVersion7().ToString() };
        var next = CreateSample(43L);

        Assert.True(deduplicator.ShouldEmit(first));
        Assert.False(deduplicator.ShouldEmit(retry));
        Assert.True(deduplicator.ShouldEmit(next));
    }

    [Fact]
    public void QualityMetadataMarksMissingOptionalValuesAsPartial()
    {
        var metadata = AcquisitionSampleMetadata.CreateQuality(
            new Dictionary<string, object?>
            {
                ["temperature"] = 21.5,
                ["pressure"] = null
            },
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
        var quality = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            metadata["quality"]);

        Assert.Equal("partial", quality["status"]);
        Assert.Equal(1, quality["missingValueCount"]);
    }

    private static ProductionEvent CreateSample(long sourceSequence)
        => ProductionEvent.Create(
            "sample.created",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/device-01",
            new ObjectRef("equipment", "device-01"),
            data: new Dictionary<string, object?>
            {
                ["sourceSequence"] = sourceSequence,
                ["values"] = new Dictionary<string, object?> { ["temperature"] = 21.5 }
            });
}
