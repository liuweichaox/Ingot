using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class CycleAnalysisMaterializerTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedCycle_IsMaterializedThenReused()
    {
        var store = new FakeStore();
        var materializer = Create(store);
        var rows = Rows();

        var first = await materializer.GetOrComputeAsync(
            "cycle-1", rows, Start, Start.AddSeconds(2), Model(), Plan());
        var second = await materializer.GetOrComputeAsync(
            "cycle-1", rows, Start, Start.AddSeconds(2), Model(), Plan());

        Assert.Equal("materialized", first.Materialization.Status);
        Assert.Equal("cached", second.Materialization.Status);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(rows.Count, second.Materialization.SourceEventCount);
        Assert.Equal(rows.Max(static row => row.IngestId), second.Materialization.SourceMaxIngestId);
    }

    [Fact]
    public async Task ActiveCycle_RemainsQueryTimeAndIsNotPersisted()
    {
        var store = new FakeStore();
        var result = await Create(store).GetOrComputeAsync(
            "cycle-1", Rows(), Start, null, Model(), Plan());

        Assert.Equal("query-time", result.Materialization.Status);
        Assert.Equal(0, store.LoadCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ConfigurationVersion_IsPartOfMaterializationIdentity()
    {
        var store = new FakeStore();
        var materializer = Create(store);
        var rows = Rows();

        await materializer.GetOrComputeAsync(
            "cycle-1", rows, Start, Start.AddSeconds(2), Model(1), Plan(1));
        var changed = await materializer.GetOrComputeAsync(
            "cycle-1", rows, Start, Start.AddSeconds(2), Model(2), Plan(2));

        Assert.Equal("materialized", changed.Materialization.Status);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task InvalidationDuringSave_DoesNotExposeStaleSnapshotAsReady()
    {
        var store = new FakeStore { InvalidateDuringSave = true };
        var result = await Create(store).GetOrComputeAsync(
            "cycle-1", Rows(), Start, Start.AddSeconds(2), Model(), Plan());

        Assert.Equal("query-time", result.Materialization.Status);
        Assert.Equal(1, store.SaveCount);
    }

    private static CycleAnalysisMaterializer Create(FakeStore store)
        => new(store, new WholeCycleAnalysisEngine(), NullLogger<CycleAnalysisMaterializer>.Instance);

    private static IReadOnlyList<PlatformProductionEvent> Rows()
        =>
        [
            Row(1, 0, 1),
            Row(2, 1000, 2),
            Row(3, 2000, 3)
        ];

    private static PlatformProductionEvent Row(long ingestId, int milliseconds, double value)
        => new()
        {
            IngestId = ingestId,
            EdgeId = "edge-a",
            IngestedAt = Start.AddMilliseconds(milliseconds),
            Event = new ProductionEvent
            {
                EventId = $"event-{ingestId}",
                EventType = "process.sample",
                EventTypeVersion = 1,
                OccurredAt = Start.AddMilliseconds(milliseconds),
                RecordedAt = Start.AddMilliseconds(milliseconds),
                Source = "test",
                Subject = new ObjectRef("equipment", "machine-a"),
                CorrelationId = "cycle-1",
                Data = new Dictionary<string, object?>
                {
                    ["values"] = new Dictionary<string, object?> { ["temperature"] = value }
                }
            }
        };

    private static ProcessDataModel Model(int version = 1)
        => new()
        {
            ModelId = "model-a",
            Version = version,
            Name = "模型",
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "temperature",
                        SourceField = "Temperature",
                        Unit = "Cel"
                    }
                ]
            }
        };

    private static ProcessAnalysisPlan Plan(int version = 1)
        => new()
        {
            PlanId = "plan-a",
            Version = version,
            Name = "方案",
            DataModelId = "model-a",
            DataModelVersion = version,
            AlignmentMode = "elapsed",
            Signals =
            [
                new AnalysisSignalSelection
                {
                    DataItemCode = "temperature",
                    Features = ["mean", "max"]
                }
            ]
        };

    private sealed class FakeStore : ICycleAnalysisMaterializationStore
    {
        private readonly Dictionary<CycleAnalysisMaterializationKey, CycleAnalysisSnapshot> _snapshots = [];

        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public bool InvalidateDuringSave { get; init; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<CycleAnalysisSnapshot?> TryLoadAsync(
            CycleAnalysisMaterializationKey key,
            long sourceMaxIngestId,
            int sourceEventCount,
            CancellationToken ct = default)
        {
            LoadCount++;
            if (_snapshots.TryGetValue(key, out var snapshot) &&
                snapshot.SourceMaxIngestId == sourceMaxIngestId &&
                snapshot.SourceEventCount == sourceEventCount)
                return Task.FromResult<CycleAnalysisSnapshot?>(snapshot);
            return Task.FromResult<CycleAnalysisSnapshot?>(null);
        }

        public Task<CycleAnalysisSnapshot> SaveAsync(
            CycleAnalysisMaterializationKey key,
            long sourceMaxIngestId,
            int sourceEventCount,
            WholeCycleAnalysisResult analysis,
            CancellationToken ct = default)
        {
            SaveCount++;
            var snapshot = new CycleAnalysisSnapshot(
                analysis,
                Start.AddMinutes(SaveCount),
                sourceMaxIngestId,
                sourceEventCount);
            if (!InvalidateDuringSave)
                _snapshots[key] = snapshot;
            return Task.FromResult(snapshot);
        }

        public Task MarkDirtyAsync(
            IReadOnlyCollection<string> correlationIds,
            long invalidatedSourceMaxIngestId,
            string reason,
            CancellationToken ct = default)
        {
            foreach (var key in _snapshots.Keys
                         .Where(key => correlationIds.Contains(key.CorrelationId, StringComparer.Ordinal))
                         .ToArray())
                _snapshots.Remove(key);
            return Task.CompletedTask;
        }
    }
}
