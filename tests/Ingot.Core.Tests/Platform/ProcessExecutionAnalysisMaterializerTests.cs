// 验证平台组件 ProcessExecutionAnalysisMaterializer 的成功、拒绝和安全边界。

using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.TimeSeries;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessExecutionAnalysisMaterializerTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedProcessExecution_IsMaterializedThenReused()
    {
        var store = new FakeStore();
        var materializer = Create(store);
        var rows = Rows();

        var first = await materializer.GetOrComputeAsync(
            "execution-1", rows, Start, Start.AddSeconds(2), Model(), Plan());
        var second = await materializer.GetOrComputeAsync(
            "execution-1", rows, Start, Start.AddSeconds(2), Model(), Plan());

        Assert.Equal("materialized", first.Materialization.Status);
        Assert.Equal("cached", second.Materialization.Status);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(rows.Count, second.Materialization.SourceEventCount);
        Assert.Equal(rows.Min(static row => row.IngestId), second.Materialization.SourceMinIngestId);
        Assert.Equal(rows.Max(static row => row.IngestId), second.Materialization.SourceMaxIngestId);
        Assert.Matches("^[a-f0-9]{64}$", second.Materialization.SourceContentHash);
    }

    [Fact]
    public async Task LatestReadyMaterialization_CanServeSummaryWithoutSourceScan()
    {
        var store = new FakeStore();
        var materializer = Create(store);
        await materializer.GetOrComputeAsync(
            "execution-1", Rows(), Start, Start.AddSeconds(2), Model(), Plan());

        var latest = await materializer.TryLoadLatestAsync(
            "execution-1", Model(), Plan());

        Assert.NotNull(latest);
        Assert.Equal(3, latest.Materialization.SourceEventCount);
        Assert.Equal("cached", latest.Materialization.Status);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task ActiveProcessExecution_RemainsQueryTimeAndIsNotPersisted()
    {
        var store = new FakeStore();
        var result = await Create(store).GetOrComputeAsync(
            "execution-1", Rows(), Start, null, Model(), Plan());

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
            "execution-1", rows, Start, Start.AddSeconds(2), Model(1), Plan(1));
        var changed = await materializer.GetOrComputeAsync(
            "execution-1", rows, Start, Start.AddSeconds(2), Model(2), Plan(2));

        Assert.Equal("materialized", changed.Materialization.Status);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task ChangedSourceContent_WithSameWatermarkAndCount_IsRecomputed()
    {
        var store = new FakeStore();
        var materializer = Create(store);
        await materializer.GetOrComputeAsync(
            "execution-1", Rows(), Start, Start.AddSeconds(2), Model(), Plan());
        var changedRows = new[] { Row(1, 0, 1), Row(2, 1000, 20), Row(3, 2000, 3) };

        var changed = await materializer.GetOrComputeAsync(
            "execution-1", changedRows, Start, Start.AddSeconds(2), Model(), Plan());

        Assert.Equal("materialized", changed.Materialization.Status);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task InvalidationDuringSave_DoesNotExposeStaleSnapshotAsReady()
    {
        var store = new FakeStore { InvalidateDuringSave = true };
        var result = await Create(store).GetOrComputeAsync(
            "execution-1", Rows(), Start, Start.AddSeconds(2), Model(), Plan());

        Assert.Equal("query-time", result.Materialization.Status);
        Assert.Equal(1, store.SaveCount);
    }

    private static ProcessExecutionAnalysisMaterializer Create(FakeStore store)
        => new(store, new ProcessExecutionAnalysisEngine(), NullLogger<ProcessExecutionAnalysisMaterializer>.Instance);

    private static IReadOnlyList<ProcessSampleFrame> Rows()
        =>
        [
            Row(1, 0, 1),
            Row(2, 1000, 2),
            Row(3, 2000, 3)
        ];

    private static ProcessSampleFrame Row(long ingestId, int milliseconds, double value)
        => new()
        {
            EventId = $"event-{ingestId}",
            IngestId = ingestId,
            OccurredAt = Start.AddMilliseconds(milliseconds),
            RecordedAt = Start.AddMilliseconds(milliseconds),
            IngestedAt = Start.AddMilliseconds(milliseconds),
            NumericValues = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["temperature"] = value
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
                        DisplayName = "Temperature",
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

    private sealed class FakeStore : IProcessExecutionAnalysisMaterializationStore
    {
        private readonly Dictionary<ProcessExecutionAnalysisMaterializationKey, ProcessExecutionAnalysisSnapshot> _snapshots = [];

        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public bool InvalidateDuringSave { get; init; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ProcessExecutionAnalysisSnapshot?> TryLoadAsync(
            ProcessExecutionAnalysisMaterializationKey key,
            ProcessExecutionAnalysisSourceFingerprint source,
            CancellationToken ct = default)
        {
            LoadCount++;
            if (_snapshots.TryGetValue(key, out var snapshot) &&
                snapshot.Source == source)
                return Task.FromResult<ProcessExecutionAnalysisSnapshot?>(snapshot);
            return Task.FromResult<ProcessExecutionAnalysisSnapshot?>(null);
        }

        public Task<ProcessExecutionAnalysisSnapshot?> TryLoadLatestAsync(
            ProcessExecutionAnalysisMaterializationKey key,
            CancellationToken ct = default)
            => Task.FromResult(_snapshots.GetValueOrDefault(key));

        public Task<ProcessExecutionAnalysisSnapshot> SaveAsync(
            ProcessExecutionAnalysisMaterializationKey key,
            ProcessExecutionAnalysisSourceFingerprint source,
            WholeProcessExecutionAnalysisResult analysis,
            CancellationToken ct = default)
        {
            SaveCount++;
            var snapshot = new ProcessExecutionAnalysisSnapshot(
                analysis,
                Start.AddMinutes(SaveCount),
                source);
            if (!InvalidateDuringSave)
                _snapshots[key] = snapshot;
            return Task.FromResult(snapshot);
        }

        public Task MarkDirtyAsync(
            IReadOnlyCollection<string> executionIds,
            long invalidatedSourceMaxIngestId,
            string reason,
            CancellationToken ct = default)
        {
            foreach (var key in _snapshots.Keys
                         .Where(key => executionIds.Contains(key.ExecutionId, StringComparer.Ordinal))
                         .ToArray())
                _snapshots.Remove(key);
            return Task.CompletedTask;
        }
    }
}
