using System.Net.NetworkInformation;
using System.Text;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class EventRuleCollectorTests
{
    [Fact]
    public async Task EdgePair_ShouldSnapshotConfiguredFieldsAndContextAtStart()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sink = new CapturingEventSink(cancellation);
        var state = new FakeStateStore();
        var collector = new EventRuleCollector(
            new HealthyHeartbeat(),
            state,
            state,
            sink,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Edge:EdgeId"] = "EDGE-TEST"
                })
                .Build(),
            Options.Create(new AcquisitionOptions
            {
                ChannelCollector = new ChannelCollectorOptions
                {
                    ConnectionCheckRetryDelayMs = 1,
                    TriggerWaitDelayMs = 1
                }
            }),
            NullLogger<EventRuleCollector>.Instance);

        var config = new DeviceConfig
        {
            SchemaVersion = 2,
            SourceCode = "POL-03-SIM",
            Asset = new ObjectRef("polishing-machine", "POL-03")
        };
        var rule = new EventRule
        {
            RuleId = "polish-cycle",
            Category = "cycle",
            ContextKeys = ["material_lot", "tooling"],
            Trigger = new EventRuleTrigger
            {
                Kind = EventTriggerKind.EdgePair,
                Tag = "D6006",
                DataType = "short"
            },
            SnapshotOnStart =
            [
                new EventSnapshotField
                {
                    FieldName = "recipe_id",
                    Tag = "D6100",
                    DataType = "string",
                    StringByteLength = 16
                }
            ]
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            collector.CollectAsync(config, rule, new SnapshotClient(), cancellation.Token));

        var evt = Assert.Single(sink.Events);
        Assert.Equal("cycle.started", evt.EventType);
        Assert.Equal("R-POLISH-V3", evt.Data["recipe_id"]);
        Assert.Equal("LOT-001", evt.Context["material_lot"]);
        Assert.Equal("TOOL-A", evt.Context["tooling"]);
        Assert.DoesNotContain("missing_context_keys", evt.Data.Keys);
    }

    [Fact]
    public async Task EdgePair_ShouldEmitRecoveredDiagnosticForPersistedActiveCycle()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var sink = new CapturingEventSink(cancellation);
        var existingCycle = new AcquisitionCycle
        {
            CycleId = "persisted-cycle",
            SourceCode = "POL-03-SIM",
            ChannelCode = "event-rule:polish-cycle",
            Measurement = "cycle"
        };
        var state = new FakeStateStore(existingCycle);
        var collector = new EventRuleCollector(
            new HealthyHeartbeat(),
            state,
            state,
            sink,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Edge:EdgeId"] = "EDGE-TEST"
                })
                .Build(),
            Options.Create(new AcquisitionOptions
            {
                ChannelCollector = new ChannelCollectorOptions
                {
                    ConnectionCheckRetryDelayMs = 1,
                    TriggerWaitDelayMs = 1
                }
            }),
            NullLogger<EventRuleCollector>.Instance);
        var config = new DeviceConfig
        {
            SchemaVersion = 2,
            SourceCode = "POL-03-SIM",
            Asset = new ObjectRef("polishing-machine", "POL-03")
        };
        var rule = new EventRule
        {
            RuleId = "polish-cycle",
            Category = "cycle",
            ContextKeys = ["material_lot", "tooling"],
            Trigger = new EventRuleTrigger
            {
                Kind = EventTriggerKind.EdgePair,
                Tag = "D6006",
                DataType = "short"
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            collector.CollectAsync(config, rule, new ActiveCycleClient(), cancellation.Token));

        var diagnostic = Assert.Single(sink.Events);
        Assert.Equal("diagnostic.cycle_recovered", diagnostic.EventType);
        Assert.Equal(existingCycle.CycleId, diagnostic.CorrelationId);
        Assert.Equal("LOT-001", diagnostic.Context["material_lot"]);
        Assert.Equal("TOOL-A", diagnostic.Context["tooling"]);
    }

    private sealed class SnapshotClient : PlcClientServiceBase
    {
        private int _triggerReads;

        public override Task<short> ReadShortAsync(string address)
            => Task.FromResult((short)(Interlocked.Increment(ref _triggerReads) == 1 ? 0 : 1));

        public override Task<string> ReadStringAsync(string address, ushort length, Encoding encoding)
            => Task.FromResult("R-POLISH-V3");
    }

    private sealed class ActiveCycleClient : PlcClientServiceBase
    {
        public override Task<short> ReadShortAsync(string address) => Task.FromResult((short)1);
    }

    private sealed class HealthyHeartbeat : IHeartbeatMonitor
    {
        public Task MonitorAsync(
            DeviceConfig config,
            IPlcTypedWriteClient client,
            CancellationToken ct = default) => Task.CompletedTask;

        public bool TryGetConnectionHealth(string sourceCode, out bool isConnected)
        {
            isConnected = true;
            return true;
        }

        public PlcConnectionStatus? GetConnectionStatus(string sourceCode) => null;
    }

    private sealed class FakeStateStore(AcquisitionCycle? initialCycle = null) : IEdgeStateStore
    {
        private AcquisitionCycle? _cycle = initialCycle;

        public AcquisitionCycle StartCycle(string sourceCode, string channelCode, string measurement)
            => _cycle = new AcquisitionCycle
            {
                CycleId = "cycle-test",
                SourceCode = sourceCode,
                ChannelCode = channelCode,
                Measurement = measurement
            };

        public AcquisitionCycle? EndCycle(string sourceCode, string channelCode, string measurement)
        {
            var cycle = _cycle;
            _cycle = null;
            return cycle;
        }

        public AcquisitionCycle? GetActiveCycle(string sourceCode, string channelCode, string measurement)
            => _cycle;

        public string? Get(ObjectRef asset, string key)
            => key switch
            {
                "material_lot" => "LOT-001",
                "tooling" => "TOOL-A",
                _ => null
            };

        public Task SetAsync(ObjectRef asset, string key, string value, CancellationToken ct = default)
            => Task.CompletedTask;

        public IReadOnlyDictionary<string, string> Snapshot(ObjectRef asset, IReadOnlyList<string> keys)
            => keys
                .Select(key => (Key: key, Value: Get(asset, key)))
                .Where(static pair => pair.Value is not null)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value!,
                    StringComparer.Ordinal);
    }

    private sealed class CapturingEventSink(CancellationTokenSource cancellation) : IEventSink
    {
        public List<ProductionEvent> Events { get; } = [];

        public ValueTask<ProductionEvent> EmitAsync(
            ProductionEvent evt,
            CancellationToken ct = default)
        {
            Events.Add(evt);
            cancellation.Cancel();
            return ValueTask.FromResult(evt with { Seq = Events.Count });
        }
    }
}
