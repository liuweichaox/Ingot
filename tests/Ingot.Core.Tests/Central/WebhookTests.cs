using System.Net;
using System.Text.Json;
using Ingot.Central.Api.Webhooks;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Central;

public sealed class WebhookTests
{
    [Fact]
    public void CloudEventMapper_ShouldProduceStructuredCloudEvent()
    {
        var item = CreateEvent();

        using var document = JsonDocument.Parse(CloudEventMapper.Serialize(item, "com.ingot"));
        var root = document.RootElement;

        Assert.Equal("1.0", root.GetProperty("specversion").GetString());
        Assert.Equal(item.Event.EventId, root.GetProperty("id").GetString());
        Assert.Equal("com.ingot.cycle.completed", root.GetProperty("type").GetString());
        Assert.Equal("POL-03", root.GetProperty("subject").GetString());
        Assert.Equal("EDGE-001", root.GetProperty("ingotedgeid").GetString());
        Assert.Equal("LOT-001",
            root.GetProperty("data").GetProperty("context").GetProperty("material_lot").GetString());
    }

    [Fact]
    public void Matcher_ShouldApplyEventSubjectAndContextFilters()
    {
        var item = CreateEvent();
        var subscription = CreateSubscription() with
        {
            EventTypes = ["cycle.completed"],
            SubjectType = "polishing-machine",
            SubjectId = "POL-03",
            Context = new Dictionary<string, string> { ["material_lot"] = "LOT-001" }
        };

        Assert.True(WebhookSubscriptionMatcher.Matches(subscription, item));
        Assert.False(WebhookSubscriptionMatcher.Matches(
            subscription with { EventTypes = ["alarm.raised"] },
            item));
    }

    [Fact]
    public async Task Dispatcher_ShouldSendCloudEventWithIdAndSignature()
    {
        var handler = new RecordingHandler();
        var dispatcher = new WebhookDispatcher(
            new SingleClientFactory(new HttpClient(handler)),
            Options.Create(new WebhookOptions { EventTypePrefix = "com.ingot" }),
            NullLogger<WebhookDispatcher>.Instance);
        var subscription = CreateSubscription() with { Secret = "test-secret" };
        var item = CreateEvent();

        var result = await dispatcher.DeliverAsync(subscription, item);

        Assert.True(result.Succeeded);
        Assert.Equal("application/cloudevents+json", handler.ContentType);
        Assert.Equal(item.Event.EventId, handler.EventId);
        Assert.Equal(
            CloudEventMapper.ComputeSignature(handler.Body, "test-secret"),
            handler.Signature);
    }

    private static CentralProductionEvent CreateEvent() => new()
    {
        IngestId = 42,
        EdgeId = "EDGE-001",
        IngestedAt = DateTimeOffset.Parse("2026-07-17T00:00:01Z"),
        Event = new ProductionEvent
        {
            EventId = "019f6c00-0000-7000-8000-000000000001",
            EventType = "cycle.completed",
            EventTypeVersion = 1,
            OccurredAt = DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            RecordedAt = DateTimeOffset.Parse("2026-07-17T00:00:00.100Z"),
            Source = "edge/EDGE-001/POL-03-SIM/polish-cycle",
            Subject = new ObjectRef("polishing-machine", "POL-03"),
            Context = new Dictionary<string, string>
            {
                ["material_lot"] = "LOT-001",
                ["tooling"] = "TOOL-A"
            },
            Data = new Dictionary<string, object?> { ["good_count"] = 1 },
            CorrelationId = "cycle-001",
            Seq = 9
        }
    };

    private static WebhookSubscription CreateSubscription() => new()
    {
        SubscriptionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        Name = "test",
        Endpoint = new Uri("https://consumer.example/events"),
        Cursor = 0,
        Enabled = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public byte[] Body { get; private set; } = [];
        public string? ContentType { get; private set; }
        public string? EventId { get; private set; }
        public string? Signature { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            ContentType = request.Content.Headers.ContentType?.MediaType;
            EventId = request.Headers.GetValues("X-Ingot-Event-Id").Single();
            Signature = request.Headers.GetValues("X-Ingot-Signature").Single();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
