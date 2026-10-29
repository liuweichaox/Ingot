// Guard reused MQTT source configurations against silently replacing another logical group's broker session.
using Ingot.Contracts.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class AcquisitionSessionCompatibilityTests
{
    [Fact]
    public void ReusedExplicitClientId_IsRejectedForAnotherTask()
    {
        var first = Task("cavity-1", "collector");
        var second = Task("cavity-2", "collector") with { Mqtt = first.Mqtt! with { Host = "BROKER.LOCAL." } };
        Assert.Contains(AcquisitionSessionCompatibility.Validate(second, [first]), error => error.Path == "mqtt.clientId");
    }

    [Fact]
    public void DefaultClientIds_AreUniqueAndSameTaskRevisionIsCompatible()
    {
        var first = Task("cavity-1", "");
        var second = Task("cavity-2", "");
        Assert.NotEqual(AcquisitionSessionCompatibility.EffectiveMqttClientId(first),
            AcquisitionSessionCompatibility.EffectiveMqttClientId(second));
        Assert.Empty(AcquisitionSessionCompatibility.Validate(second, [first]));
        Assert.Empty(AcquisitionSessionCompatibility.Validate(first with { Version = 2 }, [first]));
    }

    [Fact]
    public void DistinctBrokersAndDistinctExplicitIds_AreCompatible()
    {
        var first = Task("cavity-1", "collector-1");
        Assert.Empty(AcquisitionSessionCompatibility.Validate(Task("cavity-2", "collector-2"), [first]));
        Assert.Empty(AcquisitionSessionCompatibility.Validate(Task("cavity-2", "collector-1") with
        {
            Mqtt = first.Mqtt! with { Host = "other-broker.local" }
        }, [first]));
    }

    private static IngestionTask Task(string id, string clientId) => new()
    {
        TaskId = id, Name = id, EdgeId = "EDGE-1", DataModelId = "process", Source = "connector/mqtt",
        SubjectId = id, Protocol = AcquisitionProtocols.Mqtt,
        Mqtt = new MqttConnection { Host = "broker.local", ClientId = clientId }
    };
}
