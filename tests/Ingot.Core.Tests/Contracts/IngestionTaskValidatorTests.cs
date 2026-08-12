using Ingot.Contracts.Acquisition;
using Xunit;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Core.Tests.Contracts;

public class IngestionTaskValidatorTests
{
    private static IngestionTask Profile(string protocol, params AcquisitionValueMapping[] mappings)
        => new()
        {
            TaskId = "press01-driver",
            Version = 1,
            Name = "压机接入",
            Status = ConfigurationStatuses.Draft,
            EdgeId = "EDGE-001",
            Protocol = protocol,
            DataModelId = "press-model",
            DataModelVersion = 1,
            Source = "connector/press01",
            SubjectId = "PRESS-01",
            HttpPolling = new HttpPollingConnection { BaseUrl = "http://10.0.0.5", SnapshotPath = "/snapshot" },
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
        // MELSEC 八进制越界必须在服务端保存阶段拒绝。
        var profile = Profile(AcquisitionProtocols.MelsecA1E, Melsec("X", "18", "boolean"));
        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].melsecAddress");
    }

    [Fact]
    public void MelsecStructuredFieldsRoundTripThroughTheSelector()
    {
        var profile = Profile(AcquisitionProtocols.MelsecA1E, Melsec("D", "100", "int16"));
        Assert.True(IngestionTaskValidator.TryValidate(profile, null, out var normalized, out var errors),
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
        Assert.True(IngestionTaskValidator.TryValidate(profile, null, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Equal("coil:12:boolean", normalized!.ValueMappings[0].SourcePath);
    }

    [Fact]
    public void ProtocolsWithIntrinsicTimestampDoNotAcceptASeparateTimestampPoint()
    {
        var profile = Profile(AcquisitionProtocols.OpcUa, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "ns=2;s=Press.Temperature"
        }) with { TimestampMode = "source", TimestampPath = "ignored" };
        Assert.True(IngestionTaskValidator.TryValidate(profile, null, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Equal("source", normalized!.TimestampMode);
        Assert.Empty(normalized.TimestampPath);
    }

    [Fact]
    public void InvalidNodeIdIsRejectedBeforeReachingTheDevice()
    {
        var profile = Profile(AcquisitionProtocols.OpcUa, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "ns=abc;s=Press"
        });
        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].sourcePath");
    }

    [Theory]
    [InlineData("plant/+/line", true)]
    [InlineData("plant/#", true)]
    [InlineData("plant/a+b/line", false)]
    [InlineData("plant/#/line", false)]
    public void MqttTopicFilterWildcardsAreValidated(string topic, bool expected)
        => Assert.Equal(expected, IngestionTaskValidator.IsValidMqttTopicFilter(topic, out _));

    [Fact]
    public void PerTopicBindingIsRejectedForProtocolsThatCannotHonourIt()
    {
        var profile = Profile(AcquisitionProtocols.HttpPolling, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "sensors.temperature",
            Topic = "line/press01"
        });
        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
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

        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "mqtt.topics[0].payloadRoot");
        Assert.Contains(errors, item => item.Path == "valueMappings[0].topic");
    }

    [Theory]
    [InlineData("items[10001].value")]
    [InlineData("items[0]junk.value")]
    [InlineData("items[-1]")]
    [InlineData("/items/~2bad")]
    public void UnsafeOrMalformedJsonSelectorsAreRejectedBeforeDeployment(string selector)
    {
        var task = Profile(AcquisitionProtocols.HttpPolling, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = selector
        });

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].sourcePath");
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
        Assert.True(IngestionTaskValidator.TryValidate(profile, null, out var normalized, out var errors),
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
        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "name");
        Assert.Contains(errors, item => item.Path == "edgeId");
        Assert.Contains(errors, item => item.Path == "modbusTcp.host");
        Assert.Contains(errors, item => item.Path == "modbusTcp.port");
        Assert.Contains(errors, item => item.Path == "valueMappings");
    }

    [Fact]
    public void LifecycleMustReferenceAConfiguredContextAndUseValidEventTypes()
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
            Lifecycle = new AcquisitionLifecycleMapping
            {
                ActiveContextKey = "run_active",
                ActiveValue = string.Empty,
                StartedEventType = "Invalid Event"
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "lifecycle.activeContextKey");
        Assert.Contains(errors, item => item.Path == "lifecycle.activeValue");
        Assert.Contains(errors, item => item.Path == "lifecycle.startedEventType");
    }

    [Fact]
    public void HttpMethodIsNormalizedBeforeValidation()
    {
        var task = Profile(AcquisitionProtocols.HttpPolling, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "temperature"
        }) with { HttpPolling = new HttpPollingConnection { BaseUrl = "http://device.local", Method = " POST " } };

        Assert.True(IngestionTaskValidator.TryValidate(task, null, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Equal("post", normalized!.HttpPolling.Method);
    }

    [Theory]
    [InlineData("https://other-host.invalid/snapshot")]
    [InlineData("//other-host.invalid/snapshot")]
    [InlineData("\\\\other-host.invalid\\snapshot")]
    [InlineData("/snapshot\r\nX-Injected: value")]
    public void HttpSnapshotPathCannotOverrideTheConfiguredDeviceHost(string snapshotPath)
    {
        var task = Profile(AcquisitionProtocols.HttpPolling, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "temperature"
        }) with
        {
            HttpPolling = new HttpPollingConnection
            {
                BaseUrl = "https://device.local",
                SnapshotPath = snapshotPath
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "httpPolling.snapshotPath");
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Content-Length")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Authorization")]
    public void HttpTransportHeadersCannotBeOverridden(string header)
    {
        var task = Profile(AcquisitionProtocols.HttpPolling, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "temperature"
        }) with
        {
            HttpPolling = new HttpPollingConnection
            {
                BaseUrl = "http://device.local",
                Headers = new Dictionary<string, string> { [header] = "invalid" }
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path.Contains(header, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExplicitJsonNullsReturnValidationErrorsInsteadOfServerExceptions()
    {
        var task = Profile(AcquisitionProtocols.HttpPolling) with
        {
            HttpPolling = null!,
            Execution = null!,
            ContextMappings = [null!],
            ValueMappings = [null!]
        };

        var exception = Record.Exception(() =>
            Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors)));

        Assert.Null(exception);
    }

    [Fact]
    public void ExcessiveMappingCountIsRejectedBeforeDeepValidation()
    {
        var mapping = new AcquisitionValueMapping { DataItemCode = "temperature", SourcePath = "value" };
        var task = Profile(AcquisitionProtocols.HttpPolling) with
        {
            ValueMappings = Enumerable.Repeat(mapping, 20_001).ToArray()
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings" && item.Message.Contains("20000"));
    }

    [Fact]
    public void MultiTopicMqttRequiresFiniteSnapshotAge()
    {
        var profile = Profile(AcquisitionProtocols.Mqtt, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "value",
            Topic = "line/press01/temperature"
        }) with
        {
            Mqtt = new MqttConnection
            {
                Host = "10.0.0.9",
                Topics =
                [
                    new MqttTopicSubscription { Topic = "line/press01/temperature" },
                    new MqttTopicSubscription { Topic = "line/press01/state" }
                ]
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "mqtt.snapshotMaxAgeSeconds");
    }

    [Fact]
    public void OverlappingMqttFiltersAndInvalidTopicVariableIndicesAreRejected()
    {
        var task = Profile(AcquisitionProtocols.Mqtt, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "$topic.machine",
            Topic = "plant/+/telemetry"
        }) with
        {
            Mqtt = new MqttConnection
            {
                Host = "10.0.0.9",
                SnapshotMaxAgeSeconds = 30,
                Topics =
                [
                    new MqttTopicSubscription
                    {
                        Topic = "plant/+/telemetry",
                        TopicVariables = new Dictionary<string, int> { ["machine"] = 9 }
                    },
                    new MqttTopicSubscription { Topic = "plant/press01/#" }
                ]
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path.Contains("topicVariables.machine"));
        Assert.Contains(errors, item => item.Message.Contains("命中同一报文"));
    }

    [Fact]
    public void RegisterTimestampEncodingMustMatchSelectorWidth()
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
            TimestampMode = "source",
            TimestampEncoding = AcquisitionTimestampEncodings.UnixMilliseconds,
            TimestampPath = "holding-register:200:uint32"
        };

        Assert.False(IngestionTaskValidator.TryValidate(profile, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "timestampPath" && item.Message.Contains("不匹配"));
    }

    [Fact]
    public void ProductionOpcUaCannotAutoTrustUnknownServerCertificates()
    {
        var task = Profile(AcquisitionProtocols.OpcUa, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "ns=2;s=Press.Temperature"
        }) with
        {
            Status = ConfigurationStatuses.Published,
            OpcUa = new OpcUaConnection
            {
                EndpointUrl = "opc.tcp://device.local:4840",
                TrustServerCertificate = true
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "opcUa.trustServerCertificate");
    }

    [Fact]
    public void MqttTlsCanUseTheOperatingSystemTrustStore()
    {
        var task = Profile(AcquisitionProtocols.Mqtt, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "value",
            Topic = "plant/press01"
        }) with
        {
            Mqtt = new MqttConnection
            {
                Host = "broker.local",
                Port = 8883,
                UseTls = true,
                Topics = [new MqttTopicSubscription { Topic = "plant/press01" }]
            }
        };

        Assert.True(IngestionTaskValidator.TryValidate(task, null, out _, out var errors),
            string.Join("；", errors));
    }

    [Fact]
    public void UsernameAuthenticationRequiresASecretReference()
    {
        var task = Profile(AcquisitionProtocols.OpcUa, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "ns=2;s=Press.Temperature"
        }) with
        {
            OpcUa = new OpcUaConnection
            {
                EndpointUrl = "opc.tcp://device.local:4840",
                AuthenticationType = "username",
                Username = "operator"
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, null, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "opcUa.passwordSecretRef");
    }

    [Fact]
    public void PublishedProfileMustDeclareSourceUnitForUnitBearingModelItem()
    {
        var profile = Profile(AcquisitionProtocols.ModbusTcp, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = string.Empty,
            ModbusArea = "holding-register",
            ModbusAddress = 100,
            SourceDataType = "int16"
        }) with { Status = ConfigurationStatuses.Published };
        var model = new ProcessDataModel
        {
            ModelId = "press-model",
            Name = "Press model",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "press.temperature",
                        DisplayName = "温度",
                        Unit = "°C",
                        Nullable = false
                    }
                ]
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(profile, model, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "valueMappings[0].sourceUnit");
    }

    [Fact]
    public void DeploymentModelMustMatchTheExactTaskReference()
    {
        var task = Profile(AcquisitionProtocols.ModbusTcp, new AcquisitionValueMapping
        {
            DataItemCode = "press.temperature",
            SourcePath = "holding-register:100:int16",
            SourceDataType = "int16"
        });
        var wrongModel = new ProcessDataModel
        {
            ModelId = task.DataModelId,
            Version = task.DataModelVersion + 1,
            Name = "Wrong version",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "press.temperature",
                        DisplayName = "Temperature"
                    }
                ]
            }
        };

        Assert.False(IngestionTaskValidator.TryValidate(task, wrongModel, out _, out var errors));
        Assert.Contains(errors, item => item.Path == "dataModelId" && item.Message.Contains("部署携带"));
    }

    [Fact]
    public void DataSourceValidation_SanitizesExplicitNullCollectionsBeforeUsingThem()
    {
        var source = new DataSourceInstance
        {
            DataSourceId = "press-01",
            Name = "Press 01",
            EdgeId = "edge-01",
            Protocol = AcquisitionProtocols.HttpPolling,
            SourceKey = "connector/http/press-01",
            SubjectId = "press-01",
            HttpPolling = new HttpPollingConnection
            {
                BaseUrl = "http://press-01.local",
                SnapshotPath = "/snapshot",
                Method = "get",
                Headers = null!,
                HeaderSecretRefs = null!
            }
        };

        Assert.True(IngestionTaskValidator.TryValidateDataSource(source, out var normalized, out var errors),
            string.Join("；", errors));
        Assert.Empty(normalized!.HttpPolling!.Headers);
        Assert.Empty(normalized.HttpPolling.HeaderSecretRefs);
    }

    [Fact]
    public void TemplateValidation_ReportsExplicitNullCollectionsInsteadOfThrowing()
    {
        var template = new IngestionTaskTemplate
        {
            TemplateId = "press-template",
            Name = "Press template",
            Protocol = AcquisitionProtocols.Mqtt,
            DataModelId = "press-model",
            ValueMappings = null!,
            ContextMappings = null!
        };

        Assert.False(IngestionTaskValidator.TryValidateTemplate(template, null, out _, out var errors));
        Assert.NotEmpty(errors);
    }
}
