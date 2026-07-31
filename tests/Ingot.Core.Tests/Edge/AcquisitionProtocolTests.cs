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
    [InlineData(AcquisitionProtocols.MelsecA1E)]
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
    public void ProtocolMapper_DoesNotTreatDeviceRegistersAsCorrelationIdByDefault()
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

        Assert.Null(mapped.Sample.CorrelationId);
        Assert.Equal("30", mapped.Sample.Context["stage_number"]);
        Assert.Equal("lens-a-std@4", mapped.RecipeIdentity);
        Assert.NotNull(mapped.RecipeApplied);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            mapped.Sample.Data["values"]);
        Assert.Equal(612.5, values["upper_mold.ir_temperature"]);
    }

    [Fact]
    public void LifecycleTracker_GeneratesAndReusesCorrelationIdInsideActiveRun()
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
            ["cycle.started", "recipe.applied", "process.stage_changed", "process.sample"],
            firstEvents.Select(item => item.EventType));
        var generatedCorrelationId = firstEvents[0].CorrelationId;
        Assert.True(Guid.TryParse(generatedCorrelationId, out _));
        Assert.All(firstEvents, item => Assert.Equal(generatedCorrelationId, item.CorrelationId));
        Assert.Equal(1000, firstEvents[0].Data["pollDelayMs"]);
        Assert.False(firstEvents[0].Data.ContainsKey("expectedSampleCount"));

        var nextSample = first with
        {
            Sample = first.Sample with
            {
                EventId = Guid.CreateVersion7().ToString(),
                CorrelationId = null
            },
            RecipeApplied = null
        };
        var continuedEvents = tracker.Track(nextSample, deployment.Profile.Lifecycle, 1000);
        Assert.Equal(["process.sample"], continuedEvents.Select(item => item.EventType));
        Assert.Equal(generatedCorrelationId, continuedEvents[0].CorrelationId);
    }

    [Fact]
    public void LifecycleTracker_ExplicitInactiveSnapshot_ClosesRunWithoutCreatingPlaceholderCycle()
    {
        var deployment = Deployment();
        var lifecycle = deployment.Profile.Lifecycle! with
        {
            ActiveContextKey = "run_active",
            ActiveValue = "true"
        };
        var tracker = new AcquisitionLifecycleTracker();
        var first = ProtocolAcquisitionSnapshotMapper.Map(
            deployment with { Profile = deployment.Profile with { Lifecycle = lifecycle } },
            new Dictionary<string, object?>
            {
                ["holding-register:0"] = 25d,
                ["holding-register:100:string:24"] = "CYCLE-0001",
                ["holding-register:112:uint16"] = (ushort)10,
                ["holding-register:120:string:16"] = "lens-a-std",
                ["holding-register:128:uint16"] = (ushort)4,
                ["holding-register:160"] = 25d,
                ["run-active"] = true
            },
            "edge/EDGE-001/connector/modbus-tcp",
            null,
            DateTimeOffset.Parse("2026-07-23T08:00:00Z"));
        first = first with
        {
            Sample = first.Sample with
            {
                Context = new Dictionary<string, string>(first.Sample.Context)
                {
                    ["run_active"] = "true"
                }
            }
        };
        var started = tracker.Track(first, lifecycle, 1000);
        var generatedCorrelationId = started[0].CorrelationId;

        var inactive = first with
        {
            Sample = first.Sample with
            {
                EventId = Guid.CreateVersion7().ToString(),
                OccurredAt = DateTimeOffset.Parse("2026-07-23T08:00:10Z"),
                Context = new Dictionary<string, string>(first.Sample.Context)
                {
                    ["run_active"] = "false"
                }
            },
            RecipeApplied = null
        };

        var completed = tracker.Track(inactive, lifecycle, 1000);
        Assert.Equal(["cycle.completed"], completed.Select(item => item.EventType));
        Assert.Equal(generatedCorrelationId, completed[0].CorrelationId);
        Assert.Empty(tracker.Track(inactive, lifecycle, 1000));

        var restarted = first with
        {
            Sample = first.Sample with
            {
                EventId = Guid.CreateVersion7().ToString(),
                OccurredAt = DateTimeOffset.Parse("2026-07-23T08:00:20Z"),
                CorrelationId = null
            },
            RecipeApplied = null
        };
        var restartedEvents = tracker.Track(restarted, lifecycle, 1000);
        Assert.Equal(
            ["cycle.started", "recipe.applied", "process.stage_changed", "process.sample"],
            restartedEvents.Select(item => item.EventType));
        Assert.NotEqual(generatedCorrelationId, restartedEvents[0].CorrelationId);
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

    [Fact]
    public void ModbusSelectors_TranslateOneBasedManualAddressesToWireAddresses()
    {
        var deployment = Deployment();
        var mapping = new AcquisitionValueMapping
        {
            DataItemCode = "upper_mold.ir_temperature",
            SourcePath = "holding-register:1:int16",
            SourceDataType = "int16",
            ModbusArea = "holding-register",
            ModbusAddress = 1
        };
        deployment = deployment with
        {
            Profile = deployment.Profile with
            {
                ValueMappings = [mapping],
                Recipe = null,
                TimestampMode = "edge-received"
            }
        };

        var selectors = ModbusTcpAcquisitionRunner.BuildSelectors(deployment, "one-based");

        Assert.Equal((ushort)0, selectors[mapping.SourcePath].ModbusAddress);
    }

    [Fact]
    public void MelsecA1EFrame_MatchesFx3uEnetAdpBinaryExample()
    {
        var frame = MelsecA1EAcquisitionRunner.BuildWordReadFrame(
            " D"u8.ToArray(),
            address: 0,
            wordCount: 5,
            timer: 0x000A,
            layout: "A");

        Assert.Equal(
            new byte[] { 0x01, 0xFF, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x44, 0x05, 0x00 },
            frame);
    }

    [Fact]
    public void MelsecA1EFrame_UsesHighToLowHexFieldsForAsciiMode()
    {
        var frame = MelsecA1EAcquisitionRunner.BuildWordReadFrame(
            " D"u8.ToArray(),
            address: 100,
            wordCount: 1,
            timer: 0x0010,
            layout: "A",
            pcNumber: 0xFF,
            dataCode: "ascii");

        Assert.Equal("01FF00100000006444200001", System.Text.Encoding.ASCII.GetString(frame));
    }

    [Fact]
    public void MelsecA1EDecoder_ReadsLittleEndianRegisterValues()
    {
        var signed = MelsecA1EAcquisitionRunner.Decode(
            [0x81, 0x00, 0x85, 0xFF],
            "int16",
            1);
        var floating = MelsecA1EAcquisitionRunner.Decode(
            [0x81, 0x00, 0x00, 0x00, 0x48, 0x42],
            "float32",
            2);
        var text = MelsecA1EAcquisitionRunner.Decode(
            [0x81, 0x00, 0x4C, 0x45, 0x4E, 0x53, 0x00, 0x00],
            "string",
            3);

        Assert.Equal((short)-123, signed);
        Assert.Equal(50f, floating);
        Assert.Equal("LENS", text);
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
                },
                new AcquisitionValueMapping
                {
                    DataItemCode = "process.stage_number",
                    SourcePath = "holding-register:112:uint16"
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
                    DataItems =
                    [
                        new ProcessDataItemDefinition
                        {
                            Code = "upper_mold.ir_temperature",
                            SourceField = "Temperature",
                            DataType = "double"
                        },
                        new ProcessDataItemDefinition
                        {
                            Code = "process.stage_number",
                            SourceField = "Stage number",
                            DataType = "integer",
                            Category = "stage"
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
