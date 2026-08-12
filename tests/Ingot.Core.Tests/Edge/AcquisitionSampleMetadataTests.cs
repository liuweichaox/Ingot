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

        var now = DateTimeOffset.UtcNow;
        Assert.Equal(AcquisitionDeduplicationResult.Changed,
            deduplicator.Evaluate(first, now, TimeSpan.FromSeconds(30)));
        Assert.Equal(AcquisitionDeduplicationResult.Duplicate,
            deduplicator.Evaluate(retry, now.AddSeconds(10), TimeSpan.FromSeconds(30)));
        Assert.Equal(AcquisitionDeduplicationResult.Changed,
            deduplicator.Evaluate(next, now.AddSeconds(11), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void FrozenSourceIdentityBecomesStalledAfterConfiguredThreshold()
    {
        var deduplicator = new AcquisitionSourceDeduplicator();
        var sample = CreateSample(42L);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(AcquisitionDeduplicationResult.Changed,
            deduplicator.Evaluate(sample, now, TimeSpan.FromSeconds(30)));
        Assert.Equal(AcquisitionDeduplicationResult.Stalled,
            deduplicator.Evaluate(sample, now.AddSeconds(30), TimeSpan.FromSeconds(30)));
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
