using Ingot.Contracts.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class IngestionConfigurationCsvTests
{
    [Fact]
    public void DataSourceRoundTripPreservesQuotedTextAndConnection()
    {
        var source = new DataSourceInstance
        {
            DataSourceId = "press-01",
            Name = "Press, \"North\"",
            Status = "published",
            EdgeId = "EDGE-01",
            Protocol = AcquisitionProtocols.Mqtt,
            SourceKey = "connector/mqtt/press-01",
            SubjectType = "equipment",
            SubjectId = "PRESS-01",
            Mqtt = new MqttConnection
            {
                Host = "broker.local",
                Topics = [new MqttTopicSubscription { Channel = "telemetry", Topic = "plant/press-01/#" }]
            }
        };

        var parsed = Assert.Single(IngestionConfigurationCsv.ReadDataSources(
            IngestionConfigurationCsv.WriteDataSources([source])));

        Assert.Equal(source.Name, parsed.Name);
        Assert.Equal("broker.local", parsed.Mqtt!.Host);
        Assert.Equal("telemetry", Assert.Single(parsed.Mqtt.Topics).Channel);
    }

    [Fact]
    public void BindingRoundTripPreservesVersionReferences()
    {
        var binding = new IngestionTaskBinding
        {
            TaskId = "press-01",
            Version = 2,
            Name = "Press 01",
            TemplateId = "press-model",
            TemplateVersion = 3,
            DataSourceId = "press-source-01",
            DataSourceVersion = 4
        };

        var parsed = Assert.Single(IngestionConfigurationCsv.ReadBindings(
            IngestionConfigurationCsv.WriteBindings([binding])));

        Assert.Equal(3, parsed.TemplateVersion);
        Assert.Equal(4, parsed.DataSourceVersion);
    }

    [Fact]
    public void RejectsUnexpectedHeaderInsteadOfGuessingColumns()
        => Assert.Throws<InvalidDataException>(() =>
            IngestionConfigurationCsv.ReadBindings("task,name\r\na,b\r\n"));

    [Fact]
    public void ExportNeutralizesSpreadsheetFormulasWithoutBreakingRoundTrip()
    {
        var binding = new IngestionTaskBinding
        {
            TaskId = "press-01",
            Name = "=HYPERLINK(\"https://invalid\")",
            TemplateId = "press-model",
            DataSourceId = "press-source-01"
        };

        var csv = IngestionConfigurationCsv.WriteBindings([binding]);
        var parsed = Assert.Single(IngestionConfigurationCsv.ReadBindings(csv));

        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Equal(binding.Name, parsed.Name);
    }

    [Fact]
    public void FormulaProtectionPreservesAnIntentionalLeadingApostrophe()
    {
        var binding = new IngestionTaskBinding
        {
            TaskId = "press-01",
            Name = "'=literal",
            TemplateId = "press-model",
            DataSourceId = "press-source-01"
        };

        var parsed = Assert.Single(IngestionConfigurationCsv.ReadBindings(
            IngestionConfigurationCsv.WriteBindings([binding])));

        Assert.Equal(binding.Name, parsed.Name);
    }

    [Theory]
    [InlineData("\"press-01\"junk,1,name,draft,template,1,source,1\r\n")]
    [InlineData("press\"-01,1,name,draft,template,1,source,1\r\n")]
    public void RejectsMalformedQuoteSyntax(string row)
        => Assert.Throws<InvalidDataException>(() =>
            IngestionConfigurationCsv.ReadBindings(
                "taskId,version,name,status,templateId,templateVersion,dataSourceId,dataSourceVersion\r\n" + row));
}
