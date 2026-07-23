using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Edge.ConnectorHost.Acquisition;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionProtocolTests
{
    [Theory]
    [InlineData(AcquisitionProtocols.HttpPolling)]
    [InlineData(AcquisitionProtocols.Mqtt)]
    [InlineData(AcquisitionProtocols.OpcUa)]
    [InlineData(AcquisitionProtocols.ModbusTcp)]
    public void SupportedProtocols_AreDeclaredByTheSharedContract(string protocol)
        => Assert.True(AcquisitionProtocols.IsSupported(protocol));

    [Fact]
    public void EventFactory_AppliesConfiguredScaleAndOffset()
    {
        var profile = new AcquisitionProfile
        {
            ProfileId = "temperature",
            Name = "Temperature",
            EdgeId = "EDGE-001",
            DataModelId = "thermal",
            Source = "connector/modbus-tcp",
            SubjectId = "FURNACE-001",
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "temperature",
                    SourcePath = "holding-register:0",
                    Scale = 0.1,
                    Offset = -10
                }
            ]
        };
        var deployment = new AcquisitionDeployment
        {
            Profile = profile,
            DataModel = new ProcessDataModel
            {
                ModelId = "thermal",
                Name = "Thermal",
                Acquisition = new AcquisitionModel
                {
                    DataItems =
                    [
                        new ProcessDataItemDefinition
                        {
                            Code = "temperature",
                            SourceField = "Temperature",
                            DataType = "double"
                        }
                    ]
                }
            }
        };

        var sample = AcquisitionEventFactory.CreateSample(
            deployment,
            "edge/EDGE-001/connector/modbus-tcp",
            new Dictionary<string, object?> { ["temperature"] = 900 },
            DateTimeOffset.Parse("2026-07-23T00:00:00Z"));

        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(sample.Data["values"]);
        Assert.Equal(80d, values["temperature"]);
        Assert.Equal("temperature", sample.Context["acquisition_profile_id"]);
    }

    [Fact]
    public void SecretResolver_ReadsOnlyExplicitEnvironmentReferences()
    {
        const string name = "INGOT_TEST_ACQUISITION_SECRET";
        Environment.SetEnvironmentVariable(name, "secret-value");
        try
        {
            var resolver = new EnvironmentAcquisitionSecretResolver();
            Assert.Equal("secret-value", resolver.Resolve($"env:{name}"));
            Assert.Throws<InvalidOperationException>(() => resolver.Resolve("plain-text"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void ProtocolMapper_MapsContextRecipeAndCorrelationForRegisterProtocols()
    {
        var deployment = Deployment();
        var raw = new Dictionary<string, object?>
        {
            ["holding-register:0"] = 612.5,
            ["holding-register:100:string:24"] = "CYCLE-0001",
            ["holding-register:112:uint16"] = (ushort)30,
            ["holding-register:120:string:16"] = "lens-a-std",
            ["holding-register:128:uint16"] = (ushort)4,
            ["holding-register:160"] = 25d
        };

        var mapped = ProtocolAcquisitionSnapshotMapper.Map(
            deployment,
            raw,
            "edge/EDGE-001/connector/modbus-tcp",
            null,
            DateTimeOffset.Parse("2026-07-23T08:00:00Z"));

        Assert.Equal("CYCLE-0001", mapped.Sample.CorrelationId);
        Assert.Equal("30", mapped.Sample.Context["recipe_step"]);
        Assert.Equal("lens-a-std@4|CYCLE-0001", mapped.RecipeIdentity);
        Assert.NotNull(mapped.RecipeApplied);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            mapped.Sample.Data["values"]);
        Assert.Equal(612.5, values["upper_mold.ir_temperature"]);
    }

    [Fact]
    public void LifecycleTracker_EmitsCycleAndStepBoundariesFromConfiguredContext()
    {
        var deployment = Deployment();
        var tracker = new AcquisitionLifecycleTracker();
        var first = ProtocolAcquisitionSnapshotMapper.Map(
            deployment,
            new Dictionary<string, object?>
            {
                ["holding-register:0"] = 25d,
                ["holding-register:100:string:24"] = "CYCLE-0001",
                ["holding-register:112:uint16"] = (ushort)10,
                ["holding-register:120:string:16"] = "lens-a-std",
                ["holding-register:128:uint16"] = (ushort)4,
                ["holding-register:160"] = 25d
            },
            "edge/EDGE-001/connector/modbus-tcp",
            null,
            DateTimeOffset.Parse("2026-07-23T08:00:00Z"));
        var firstEvents = tracker.Track(first, deployment.Profile.Lifecycle, 1000);
        Assert.Equal(
            ["cycle.started", "recipe.applied", "recipe.step_changed", "process.sample"],
            firstEvents.Select(item => item.EventType));

        var nextCycle = first with
        {
            Sample = first.Sample with
            {
                EventId = Guid.CreateVersion7().ToString(),
                CorrelationId = "CYCLE-0002",
                Context = new Dictionary<string, string>(first.Sample.Context)
                {
                    ["correlation_id"] = "CYCLE-0002"
                }
            },
            RecipeApplied = null
        };
        var boundaryEvents = tracker.Track(nextCycle, deployment.Profile.Lifecycle, 1000);
        Assert.Equal(
            ["cycle.completed", "cycle.started", "recipe.step_changed", "process.sample"],
            boundaryEvents.Select(item => item.EventType));
        Assert.Equal("CYCLE-0001", boundaryEvents[0].CorrelationId);
        Assert.Equal(1L, boundaryEvents[0].Data["sampleCount"]);
    }

    [Fact]
    public void ModbusDecoder_ReadsUtf8RegisterStrings()
    {
        var registers = new ushort[] { 0x4359, 0x434C, 0x452D, 0x3031, 0x0000 };
        var value = ModbusTcpAcquisitionRunner.Decode(registers, new AcquisitionValueMapping
        {
            DataItemCode = "cycle.id",
            SourcePath = "holding-register:100",
            SourceDataType = "string",
            ModbusArea = "holding-register",
            ModbusAddress = 100,
            ModbusQuantity = 5
        });
        Assert.Equal("CYCLE-01", value);
    }

    private static AcquisitionDeployment Deployment()
    {
        var profile = new AcquisitionProfile
        {
            ProfileId = "optical",
            Name = "Optical",
            EdgeId = "EDGE-001",
            Protocol = AcquisitionProtocols.ModbusTcp,
            DataModelId = "optical",
            Source = "connector/modbus-tcp",
            SubjectId = "PRESS-01",
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "upper_mold.ir_temperature",
                    SourcePath = "holding-register:0"
                }
            ],
            ContextMappings =
            [
                new AcquisitionContextMapping
                {
                    ContextKey = "correlation_id",
                    SourcePath = "holding-register:100:string:24",
                    Required = true
                },
                new AcquisitionContextMapping
                {
                    ContextKey = "recipe_step",
                    SourcePath = "holding-register:112:uint16",
                    Required = true
                }
            ],
            Recipe = new AcquisitionRecipeMapping
            {
                IdPath = "holding-register:120:string:16",
                VersionPath = "holding-register:128:uint16",
                ParametersPath = ".",
                ParameterMappings =
                [
                    new AcquisitionValueMapping
                    {
                        DataItemCode = "position.heat",
                        SourcePath = "holding-register:160"
                    }
                ]
            },
            Lifecycle = new AcquisitionLifecycleMapping
            {
                CorrelationIdContextKey = "correlation_id",
                StepContextKey = "recipe_step",
                ExpectedDurationMs = 600000
            }
        };
        return new AcquisitionDeployment
        {
            Profile = profile,
            DataModel = new ProcessDataModel
            {
                ModelId = "optical",
                Name = "Optical",
                Acquisition = new AcquisitionModel
                {
                    SamplePeriodMs = 1000,
                    DataItems =
                    [
                        new ProcessDataItemDefinition
                        {
                            Code = "upper_mold.ir_temperature",
                            SourceField = "Temperature",
                            DataType = "double"
                        }
                    ]
                },
                RecipeParameters =
                [
                    new RecipeParameterDefinition
                    {
                        Code = "position.heat",
                        SourceField = "Heat",
                        DataType = "double"
                    }
                ]
            }
        };
    }
}
