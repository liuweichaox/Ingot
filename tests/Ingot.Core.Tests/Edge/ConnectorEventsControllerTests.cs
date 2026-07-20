using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Edge.ConnectorHost.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class ConnectorEventsControllerTests
{
    [Fact]
    public async Task Ingest_NormalizesPersistenceFieldsBeforeValidation()
    {
        var sink = new CapturingEventSink();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
                ["ConnectorHost:MaxBatchSize"] = "1000"
            }).Build();
        var controller = new ConnectorEventsController(sink, new StubEdgeIdentityProvider(), configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers.Authorization = "Bearer connector-token-with-at-least-24-characters";
        var incoming = new ProductionEvent
        {
            EventId = Guid.CreateVersion7().ToString(),
            EventType = "cycle.started",
            OccurredAt = DateTimeOffset.UtcNow,
            RecordedAt = default,
            Source = "connector/SOURCE-01",
            Subject = new ObjectRef("asset", "FURNACE-01")
        };

        var result = await controller.Ingest([incoming], CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(sink.Captured);
        Assert.NotEqual(default, sink.Captured.RecordedAt);
        Assert.Equal(0, sink.Captured.Seq);
        Assert.Equal("edge/EDGE-001/connector/SOURCE-01", sink.Captured.Source);
    }

    private sealed class CapturingEventSink : IEventSink
    {
        public ProductionEvent? Captured { get; private set; }

        public ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default)
        {
            Captured = evt;
            return ValueTask.FromResult(evt with { Seq = 1 });
        }
    }

    private sealed class StubEdgeIdentityProvider : IEdgeIdentityProvider
    {
        public string GetEdgeId() => "EDGE-001";
    }
}
