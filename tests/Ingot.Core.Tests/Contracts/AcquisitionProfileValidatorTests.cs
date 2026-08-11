using Ingot.Contracts.Acquisition;
using Xunit;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Core.Tests.Contracts;

public class AcquisitionProfileValidatorTests
{
    private static AcquisitionProfile Profile(string protocol, params AcquisitionValueMapping[] mappings)
        => new()
        {
            ProfileId = "press01-driver",
            Version = 1,
            Name = "压机接入",
            Status = ConfigurationStatuses.Draft,
            EdgeId = "EDGE-001",
            Protocol = protocol,
            DataModelId = "press-model",
            DataModelVersion = 1,
            Source = "connector/press01",
            SubjectId = "PRESS-01",
            Connection = new HttpPollingConnection { BaseUrl = "http://10.0.0.5", SnapshotPath = "/snapshot" },
            ModbusTcp = new ModbusTcpConnection { Host = "10.0.0.6" },
            MelsecA1E = new McA1EConnection { Host = "10.0.0.7" },
            OpcUa = new OpcUaConnection { EndpointUrl = "opc.tcp://10.0.0.8:4840" },
            Mqtt = new MqttConnection { Host = "10.0.0.9", Topics = [new MqttTopicSubscription { Topic = "line/press01" }] },
            TimestampMode = "edge-received",
            ValueMappings = mappings
        };

    private static AcquisitionValueMapping Melsec(string device, string address, string type = "int16")
        => new()
        {
            DataItemCode = "press.temperature",
            SourcePath = string.Empty,
            MelsecDevice = device,
            MelsecAddress = address,
            SourceDataType = type
        };

    [Fact]
    public void MelsecSelectorSyntaxIsRejectedByThePlatformNotOnlyByTheEdge()
    {
        // 以前 MELSEC 选择器完全没有服务端校验，八进制越界要等到设备连接才报错。
        var profile = Profile(AcquisitionProtocols.MelsecA1E, Melsec("X", "18", "boolean"));
        Assert.False(AcquisitionProfileValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].melsecAddress");
    }

    [Fact]
    public void MelsecStructuredFieldsRoundTripThroughTheSelector()
    {
        var profile = Profile(AcquisitionProtocols.MelsecA1E, Melsec("D", "100", "int16"));
        Assert.True(AcquisitionProfileValidator.TryValidate(profile, null, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Equal("D:100:int16", normalized!.ValueMappings[0].SourcePath);
        Assert.Equal("D", normalized.ValueMappings[0].MelsecDevice);
    }

    [Fact]
    public void ModbusCoilAcceptsBooleanWhichTheOldValidatorRejected()
    {
        var profile = Profile(AcquisitionProtocols.ModbusTcp, new AcquisitionValueMapping
        {
            DataItemCode = "press.running",
            SourcePath = string.Empty,
            ModbusArea = "coil",
            ModbusAddress = 12,
            SourceDataType = "boolean"
        });
        Assert.True(AcquisitionProfileValidator.TryValidate(profile, null, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Equal("coil:12:boolean", normalized!.ValueMappings[0].SourcePath);
    }

    [Fact]
    public void ProtocolsWithoutServerTimestampSupportAreNormalisedInsteadOfSilentlyDropped()
    {
        var profile = Profile(AcquisitionProtocols.OpcUa, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "ns=2;s=Press.Temperature"
        }) with { TimestampMode = "source", TimestampPath = "ignored" };
        Assert.True(AcquisitionProfileValidator.TryValidate(profile, null, out var normalized, out var errors),
            string.Join("；", errors));
        // OPC UA 的采样时间只能来自服务器，配置被规范化而不是保留一个不会生效的取值。
        Assert.Equal("edge-received", normalized!.TimestampMode);
    }

    [Fact]
    public void InvalidNodeIdIsRejectedBeforeReachingTheDevice()
    {
        var profile = Profile(AcquisitionProtocols.OpcUa, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "ns=abc;s=Press"
        });
        Assert.False(AcquisitionProfileValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].sourcePath");
    }

    [Theory]
    [InlineData("plant/+/line", true)]
    [InlineData("plant/#", true)]
    [InlineData("plant/a+b/line", false)]
    [InlineData("plant/#/line", false)]
    public void MqttTopicFilterWildcardsAreValidated(string topic, bool expected)
        => Assert.Equal(expected, AcquisitionProfileValidator.IsValidMqttTopicFilter(topic, out _));

    [Fact]
    public void PerTopicBindingIsRejectedForProtocolsThatCannotHonourIt()
    {
        var profile = Profile(AcquisitionProtocols.HttpPolling, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "sensors.temperature",
            Topic = "line/press01"
        });
        Assert.False(AcquisitionProfileValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].topic");
    }

    [Fact]
    public void MqttTopicBindingsMustReferenceConfiguredTopicsAndValidPayloadRoots()
    {
        var profile = Profile(AcquisitionProtocols.Mqtt, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "value",
            Topic = "line/unknown"
        }) with
        {
            Mqtt = new MqttConnection
            {
                Host = "10.0.0.9",
                Topics =
                [
                    new MqttTopicSubscription
                    {
                        Topic = "line/press01",
                        PayloadRoot = ".payload"
                    }
                ]
            }
        };

        Assert.False(AcquisitionProfileValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "mqtt.topics[0].payloadRoot");
        Assert.Contains(errors, item => item.Path == "valueMappings[0].topic");
    }

    [Fact]
    public void ControlParametersPathIsNoLongerRequiredForRegisterProtocols()
    {
        var profile = Profile(AcquisitionProtocols.ModbusTcp, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = string.Empty,
            ModbusArea = "holding-register",
            ModbusAddress = 100,
            SourceDataType = "int16"
        }) with
        {
            ProcessSpecification = new AcquisitionProcessSpecificationMapping
            {
                IdPath = "holding-register:200:uint16",
                VersionPath = "holding-register:201:uint16"
            }
        };
        Assert.True(AcquisitionProfileValidator.TryValidate(profile, null, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Equal(".", normalized!.ProcessSpecification!.ParametersPath);
    }

    [Fact]
    public void ErrorsArePerFieldSoTheEditorCanHighlightThem()
    {
        var profile = Profile(AcquisitionProtocols.ModbusTcp) with
        {
            Name = string.Empty,
            EdgeId = string.Empty,
            ModbusTcp = new ModbusTcpConnection { Host = string.Empty, Port = 0 }
        };
        Assert.False(AcquisitionProfileValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "name");
        Assert.Contains(errors, item => item.Path == "edgeId");
        Assert.Contains(errors, item => item.Path == "modbusTcp.host");
        Assert.Contains(errors, item => item.Path == "modbusTcp.port");
        Assert.Contains(errors, item => item.Path == "valueMappings");
    }
}
