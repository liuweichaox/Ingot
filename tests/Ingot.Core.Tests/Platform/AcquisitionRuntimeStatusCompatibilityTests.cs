// Verify old Edge status JSON stays distinguishable after Platform normalizes and serializes it.
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class AcquisitionRuntimeStatusCompatibilityTests
{
    [Fact]
    public void LegacyStatusWithoutCorrelationCapability_RemainsLegacyAfterRoundTrip()
    {
        const string legacyStatus = """
            {"enabled":true,"state":"running","reportedAt":"2026-10-31T09:20:00Z","tasks":[],"deployments":[]}
            """;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var fromEdge = JsonSerializer.Deserialize<EdgeAcquisitionRuntimeStatus>(legacyStatus, options)!;
        var storedAndReturned = JsonSerializer.Deserialize<EdgeAcquisitionRuntimeStatus>(
            JsonSerializer.Serialize(fromEdge, options),
            options)!;

        Assert.False(fromEdge.SupportsSampleEventIdCorrelation);
        Assert.False(storedAndReturned.SupportsSampleEventIdCorrelation);
    }
}
