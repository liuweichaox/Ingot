// 验证物理设备身份优先级与旧设备 Subject 回退边界。

using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class PhysicalEquipmentIdentityTests
{
    [Fact]
    public void ExplicitContextEquipmentTakesPrecedenceOverLogicalEquipmentSubject()
    {
        var evt = Event("equipment", "PRESS-01:CAVITY-2", " PRESS-01 ");

        Assert.Equal("PRESS-01", PhysicalEquipmentIdentity.Resolve(evt));
    }

    [Fact]
    public void LegacyEquipmentSubjectIsUsedWhenContextHasNoEquipment()
    {
        var evt = Event("equipment", "PRESS-01", null);

        Assert.Equal("PRESS-01", PhysicalEquipmentIdentity.Resolve(evt));
    }

    [Fact]
    public void NonEquipmentSubjectDoesNotImplyPhysicalEquipment()
    {
        var evt = Event("cavity", "PRESS-01", null);

        Assert.Equal(string.Empty, PhysicalEquipmentIdentity.Resolve(evt));
    }

    [Fact]
    public void ExecutionResolutionPrefersExplicitMachineAcrossLogicalCavityEvents()
    {
        var events = new[]
        {
            Event("equipment", "PRESS-01:CAVITY-1", null),
            Event("cavity", "PRESS-01:CAVITY-2", "PRESS-01")
        };

        Assert.Equal("PRESS-01", PhysicalEquipmentIdentity.Resolve(events));
    }

    private static ProductionEvent Event(string subjectType, string subjectId, string? equipmentId)
        => ProductionEvent.Create(
            "process.execution.completed",
            DateTimeOffset.UtcNow,
            "edge/test",
            new ObjectRef(subjectType, subjectId),
            context: equipmentId is null
                ? null
                : new Dictionary<string, string> { ["equipment_id"] = equipmentId });
}
