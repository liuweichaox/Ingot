// 验证边缘组件 AcquisitionProtocol 的协议、状态和失败边界。

using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Edge.ConnectorHost.Acquisition;
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
    public void ProtocolMapper_DoesNotTreatDeviceRegistersAsExecutionIdByDefault()
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

        Assert.Null(mapped.Sample.ExecutionId);
        Assert.Equal("30", mapped.Sample.Context["stage_number"]);
        Assert.Equal("lens-a-std@4", mapped.ProcessSpecificationIdentity);
        Assert.NotNull(mapped.ProcessSpecificationApplied);
        Assert.Equal(
            new AppliedConfigurationRef("ingestion-task", "optical", 1),
            mapped.Sample.AppliedConfiguration);
        Assert.True(ProductionEventIntegrity.HasValidPayloadHash(mapped.Sample));
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            mapped.Sample.Data["values"]);
        Assert.Equal(612.5, values["upper_mold.ir_temperature"]);
    }

    [Fact]
    public void LifecycleTracker_GeneratesAndReusesExecutionIdInsideActiveRun()
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
        var firstEvents = tracker.Track(first, deployment.Task.Lifecycle, 1000);
        Assert.Equal(
            ["process.execution.started", "process.specification.applied", "process.stage_changed", "process.sample"],
            firstEvents.Select(item => item.EventType));
        var generatedExecutionId = firstEvents[0].ExecutionId;
        Assert.True(Guid.TryParse(generatedExecutionId, out _));
        Assert.All(firstEvents, item => Assert.Equal(generatedExecutionId, item.ExecutionId));
        Assert.Equal(1000, firstEvents[0].Data["pollDelayMs"]);
        Assert.False(firstEvents[0].Data.ContainsKey("expectedSampleCount"));

        var nextSample = first with
        {
            Sample = first.Sample with
            {
                EventId = Guid.CreateVersion7().ToString(),
                ExecutionId = null
            },
            ProcessSpecificationApplied = null
        };
        var continuedEvents = tracker.Track(nextSample, deployment.Task.Lifecycle, 1000);
        Assert.Equal(["process.sample"], continuedEvents.Select(item => item.EventType));
        Assert.Equal(generatedExecutionId, continuedEvents[0].ExecutionId);
    }

    [Fact]
    public void LifecycleTracker_ExplicitInactiveSnapshot_ClosesRunWithoutCreatingPlaceholderProcessExecution()
    {
        var deployment = Deployment();
        var lifecycle = deployment.Task.Lifecycle! with
        {
            ActiveContextKey = "run_active",
            ActiveValue = "true"
        };
        var tracker = new AcquisitionLifecycleTracker();
        var first = ProtocolAcquisitionSnapshotMapper.Map(
            deployment with { Task = deployment.Task with { Lifecycle = lifecycle } },
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
        var generatedExecutionId = started[0].ExecutionId;

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
            ProcessSpecificationApplied = null
        };

        var completed = tracker.Track(inactive, lifecycle, 1000);
        Assert.Equal(["process.execution.completed"], completed.Select(item => item.EventType));
        Assert.Equal(generatedExecutionId, completed[0].ExecutionId);
        Assert.Empty(tracker.Track(inactive, lifecycle, 1000));

        var restarted = first with
        {
            Sample = first.Sample with
            {
                EventId = Guid.CreateVersion7().ToString(),
                OccurredAt = DateTimeOffset.Parse("2026-07-23T08:00:20Z"),
                ExecutionId = null
            },
            ProcessSpecificationApplied = null
        };
        var restartedEvents = tracker.Track(restarted, lifecycle, 1000);
        Assert.Equal(
            ["process.execution.started", "process.specification.applied", "process.stage_changed", "process.sample"],
            restartedEvents.Select(item => item.EventType));
        Assert.NotEqual(generatedExecutionId, restartedEvents[0].ExecutionId);

        var afterConnectorRestart = new AcquisitionLifecycleTracker().Track(restarted, lifecycle, 1000);
        Assert.Equal(
            "active_at_connector_start",
            afterConnectorRestart[0].Data["lifecycleCaptureStatus"]);
    }

    [Fact]
    public void ModbusDecoder_ReadsUtf8RegisterStrings()
    {
        var registers = new ushort[] { 0x4359, 0x434C, 0x452D, 0x3031, 0x0000 };
        var value = ModbusTcpAcquisitionRunner.Decode(registers, new AcquisitionValueMapping
        {
            DataItemCode = "execution.id",
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
            Task = deployment.Task with
            {
                ValueMappings = [mapping],
                ProcessSpecification = null,
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
            MelsecPoint("int16", 1));
        var floating = MelsecA1EAcquisitionRunner.Decode(
            [0x81, 0x00, 0x00, 0x00, 0x48, 0x42],
            MelsecPoint("float32", 2));
        var text = MelsecA1EAcquisitionRunner.Decode(
            [0x81, 0x00, 0x4C, 0x45, 0x4E, 0x53, 0x00, 0x00],
            MelsecPoint("string", 3));

        Assert.Equal((short)-123, signed);
        Assert.Equal(50f, floating);
        Assert.Equal("LENS", text);
    }

    [Fact]
    public void MelsecA1EErrorHeaderIsRejectedBeforeWaitingForPayload()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MelsecA1EAcquisitionRunner.EnsureBinarySuccess([0x81, 0x5B]));

        Assert.Contains("0x5B", error.Message);
    }

    [Fact]
    public void ModbusReadBatchDoesNotCrossConfiguredAddressGap()
    {
        var selectors = new Dictionary<string, AcquisitionValueMapping>
        {
            ["holding-register:0:uint16"] = new()
            {
                DataItemCode = "a",
                SourcePath = "holding-register:0:uint16",
                ModbusArea = "holding-register",
                ModbusAddress = 0,
                SourceDataType = "uint16"
            },
            ["holding-register:120:uint16"] = new()
            {
                DataItemCode = "b",
                SourcePath = "holding-register:120:uint16",
                ModbusArea = "holding-register",
                ModbusAddress = 120,
                SourceDataType = "uint16"
            }
        }.OrderBy(item => item.Value.ModbusAddress).ToArray();

        var batch = ModbusTcpAcquisitionRunner.BuildNextReadBatch(selectors, 125, maxMergeGap: 8);

        Assert.Single(batch);
        Assert.Equal("holding-register:0:uint16", batch[0].Key);
    }

    [Fact]
    public void TimestampParserDistinguishesUnixSecondsFromMilliseconds()
    {
        const long unixSeconds = 1_800_000_000;

        var timestamp = AcquisitionTimestampParser.Parse(
            unixSeconds,
            AcquisitionTimestampEncodings.UnixSeconds,
            "D:200:int32",
            maximumFutureSkewMs: 0);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(unixSeconds), timestamp);
    }

    [Fact]
    public void TimestampParserRejectsImplausibleFutureDeviceTime()
    {
        var receivedAt = DateTimeOffset.Parse("2026-08-12T00:00:00Z");
        var raw = receivedAt.AddMinutes(10).ToUnixTimeMilliseconds();

        var error = Assert.Throws<InvalidDataException>(() => AcquisitionTimestampParser.Parse(
            raw,
            AcquisitionTimestampEncodings.UnixMilliseconds,
            "clock",
            receivedAt,
            maximumFutureSkewMs: 300_000));

        Assert.Contains("超前", error.Message);
    }

    private static AcquisitionSelectors.MelsecPoint MelsecPoint(string dataType, int wordCount)
    {
        Assert.True(AcquisitionSelectors.TryGetMelsecDevice("D", out var device));
        return new AcquisitionSelectors.MelsecPoint(device!, 0, "0", dataType, wordCount, null, null);
    }

    private static AcquisitionDeployment Deployment()
    {
        var profile = new IngestionTask
        {
            TaskId = "optical",
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
            ProcessSpecification = new AcquisitionProcessSpecificationMapping
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
            Task = profile,
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
                            DisplayName = "Temperature",
                            DataType = "double"
                        },
                        new ProcessDataItemDefinition
                        {
                            Code = "process.stage_number",
                            DisplayName = "Stage number",
                            DataType = "integer",
                            Category = "stage"
                        }
                    ]
                },
                ControlParameters =
                [
                    new ControlParameterDefinition
                    {
                        Code = "position.heat",
                        DisplayName = "Heat",
                        DataType = "double"
                    }
                ]
            }
        };
    }
}
