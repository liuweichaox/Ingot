// Verify that shared acquisition configuration keeps logical runs independent from physical machine context.
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionLogicalSubjectTests
{
    [Fact]
    public void RegisterAndJsonMapping_KeepLogicalSubjectAndExplicitMachine()
    {
        var deployment = Deployment("UNIT-01");
        var raw = new Dictionary<string, object?> { ["temperature"] = 650d, ["active"] = true };
        var register = ProtocolAcquisitionSnapshotMapper.Map(deployment, raw, "edge/EDGE-1/plc-a", null, DateTimeOffset.UtcNow);
        Assert.Equal("UNIT-01", register.Sample.Subject.Id);
        Assert.Equal("M01", register.Sample.Context["equipment_id"]);

        var options = JsonAcquisitionOptionsFactory.Create(deployment);
        using var snapshot = JsonDocument.Parse("{\"temperature\":650,\"active\":true}");
        var json = HttpPollingSnapshotMapper.Map(snapshot.RootElement, options, "edge/EDGE-1/gateway", null);
        Assert.Equal("UNIT-01", json.Sample.Subject.Id);
        Assert.Equal("M01", json.Sample.Context["equipment_id"]);
    }

    [Fact]
    public void SixCavities_HaveIndependentExecutionIdsAndBoundaries()
    {
        var trackers = Enumerable.Range(1, 6).Select(_ => new AcquisitionLifecycleTracker()).ToArray();
        var ids = new HashSet<string>();
        for (var index = 0; index < trackers.Length; index++)
        {
            var cavity = $"M01C{index + 1:00}";
            var deployment = Deployment(cavity);
            var source = index < 3 ? "edge/EDGE-1/plc-a" : "edge/EDGE-1/plc-b";
            var idle = Map(deployment, source, false);
            Assert.Empty(trackers[index].Track(idle, deployment.Task.Lifecycle, 500));
            var events = trackers[index].Track(Map(deployment, source, true), deployment.Task.Lifecycle, 500);
            var started = Assert.Single(events, item => item.EventType == "process.execution.started");
            Assert.True(ids.Add(started.ExecutionId!));
            Assert.All(events, item =>
            {
                Assert.Equal(cavity, item.Subject.Id);
                Assert.Equal("M01", item.Context["equipment_id"]);
            });
        }

        var first = Deployment("UNIT-01");
        var completed = trackers[0].Track(Map(first, "edge/EDGE-1/plc-a", false), first.Task.Lifecycle, 500);
        Assert.Equal("process.execution.completed", Assert.Single(completed).EventType);
        Assert.False(trackers[0].IsRunActive);
        Assert.All(trackers.Skip(1), tracker => Assert.True(tracker.IsRunActive));
    }

    [Fact]
    public void LegacyEquipmentSubject_StillSuppliesItsOwnEquipmentContext()
    {
        var deployment = Deployment("PRESS-OLD");
        deployment = deployment with { Task = deployment.Task with { EquipmentId = null } };
        var mapped = Map(deployment, "edge/EDGE-1/legacy", true);
        Assert.Equal("PRESS-OLD", mapped.Sample.Context["equipment_id"]);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("123")]
    [InlineData("-7")]
    public void NonzeroProductionCounter_StartsAndEndsWithoutGuessingABit(string counter)
    {
        var deployment = Deployment("UNIT-01");
        var lifecycle = deployment.Task.Lifecycle! with { ActiveCondition = "nonzero" };
        var tracker = new AcquisitionLifecycleTracker();
        AcquisitionMappingResult Snapshot(string value) => Map(deployment, "edge/EDGE-1/plc-a", false) with
        {
            Sample = Map(deployment, "edge/EDGE-1/plc-a", false).Sample with
            {
                Context = new Dictionary<string, string> { ["run_active"] = value, ["equipment_id"] = "M01" }
            }
        };
        Assert.Empty(tracker.Track(Snapshot("0"), lifecycle, 500));
        var started = tracker.Track(Snapshot(counter), lifecycle, 500);
        Assert.Contains(started, item => item.EventType == "process.execution.started");
        Assert.Throws<InvalidDataException>(() => tracker.Track(Snapshot("not-a-number"), lifecycle, 500));
        Assert.True(tracker.IsRunActive);
        var completed = Assert.Single(tracker.Track(Snapshot("0"), lifecycle, 500));
        Assert.Equal("process.execution.completed", completed.EventType);
        Assert.Equal("completed", completed.Data["completionStatus"]);
        Assert.False(tracker.IsRunActive);
    }

    private static AcquisitionMappingResult Map(AcquisitionDeployment deployment, string source, bool active)
        => ProtocolAcquisitionSnapshotMapper.Map(deployment,
            new Dictionary<string, object?> { ["temperature"] = 650d, ["active"] = active },
            source, null, DateTimeOffset.UtcNow);

    private static AcquisitionDeployment Deployment(string subjectId) => new()
    {
        Task = new IngestionTask
        {
            TaskId = subjectId.ToLowerInvariant(), Name = subjectId, EdgeId = "EDGE-1",
            DataModelId = "press", Source = "connector/press", SubjectId = subjectId,
            EquipmentId = "M01", TimestampMode = "edge-received",
            HttpPolling = new HttpPollingConnection { BaseUrl = "http://gateway.local" },
            ContextMappings = [new AcquisitionContextMapping { ContextKey = "run_active", SourcePath = "active", Required = true }],
            ValueMappings = [new AcquisitionValueMapping { DataItemCode = "temperature", SourcePath = "temperature" }],
            Lifecycle = new AcquisitionLifecycleMapping { ActiveContextKey = "run_active" }
        },
        DataModel = new ProcessDataModel
        {
            ModelId = "press", Name = "Press", Acquisition = new AcquisitionModel
            {
                DataItems = [new ProcessDataItemDefinition { Code = "temperature", DisplayName = "Temperature", DataType = "double" }]
            }
        }
    };
}
