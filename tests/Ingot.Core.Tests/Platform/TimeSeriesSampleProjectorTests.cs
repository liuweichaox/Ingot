using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.TimeSeries;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class TimeSeriesSampleProjectorTests
{
    [Fact]
    public void Project_creates_typed_samples_with_unit_and_phase()
    {
        var evt = CreateEvent(
            new Dictionary<string, object?>
            {
                ["temperature"] = 618.5,
                ["process.stage_number"] = 20L,
                ["heater_enabled"] = true,
                ["mode"] = "soak"
            },
            new Dictionary<string, object?>
            {
                ["temperature"] = "uncertain"
            });

        var samples = TimeSeriesSampleProjector.Project(
            "EDGE-01",
            42,
            DateTimeOffset.Parse("2026-07-24T12:00:02Z"),
            evt,
            CreateAnalysis());

        Assert.Equal(4, samples.Count);
        var temperature = Assert.Single(samples, sample => sample.SignalCode == "temperature");
        Assert.Equal(618.5, temperature.NumericValue);
        Assert.Equal("°C", temperature.Unit);
        Assert.Equal("20", temperature.PhaseCode);
        Assert.Equal(SignalQualityCodes.Uncertain, temperature.QualityCode);
        Assert.Equal("edge-01/device/furnace-01/temperature", temperature.CollectionPointId);
        Assert.Equal(20, Assert.Single(samples, sample => sample.SignalCode == "process.stage_number").IntegerValue);
        Assert.True(Assert.Single(samples, sample => sample.SignalCode == "heater_enabled").BooleanValue);
        Assert.Equal("soak", Assert.Single(samples, sample => sample.SignalCode == "mode").TextValue);
    }

    [Fact]
    public void Project_ignores_null_values_without_losing_other_measurements()
    {
        var evt = CreateEvent(new Dictionary<string, object?>
        {
            ["temperature"] = null,
            ["process.stage_number"] = 30L,
            ["heater_enabled"] = false,
            ["mode"] = "press"
        });

        var samples = TimeSeriesSampleProjector.Project(
            "EDGE-01",
            43,
            DateTimeOffset.Parse("2026-07-24T12:00:02Z"),
            evt,
            CreateAnalysis());

        Assert.DoesNotContain(samples, sample => sample.SignalCode == "temperature");
        Assert.Equal(3, samples.Count);
    }

    [Fact]
    public void Project_returns_empty_for_non_sample_events_or_missing_configuration()
    {
        var evt = CreateEvent(new Dictionary<string, object?> { ["temperature"] = 600d }) with
        {
            EventType = "process.execution.completed"
        };

        Assert.Empty(TimeSeriesSampleProjector.Project(
            "EDGE-01",
            44,
            DateTimeOffset.Parse("2026-07-24T12:00:02Z"),
            evt,
            CreateAnalysis()));
        Assert.Empty(TimeSeriesSampleProjector.Project(
            "EDGE-01",
            44,
            DateTimeOffset.Parse("2026-07-24T12:00:02Z"),
            evt with { EventType = "process.sample" },
            null));
    }

    private static ProductionEvent CreateEvent(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, object?>? quality = null)
        => new()
        {
            EventId = Guid.CreateVersion7().ToString(),
            EventType = "process.sample",
            OccurredAt = DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
            RecordedAt = DateTimeOffset.Parse("2026-07-24T12:00:01Z"),
            Source = "edge/EDGE-01/FURNACE-01",
            Subject = new ObjectRef("device", "FURNACE-01"),
            ExecutionId = "RUN-001",
            Seq = 100,
            Context = new Dictionary<string, string>
            {
                ["data_model_id"] = "heat-treatment",
                ["data_model_version"] = "2",
                ["product_family_code"] = "series-a",
                ["stage_number"] = "20"
            },
            Data = new Dictionary<string, object?>
            {
                ["values"] = values,
                ["quality"] = quality ?? new Dictionary<string, object?>()
            }
        };

    private static ResolvedProcessAnalysis CreateAnalysis()
        => new()
        {
            DataModel = new ProcessDataModel
            {
                ModelId = "heat-treatment",
                Version = 2,
                Name = "热处理",
                Status = ConfigurationStatuses.Published,
                Acquisition = new AcquisitionModel
                {
                    DataItems =
                    [
                        new ProcessDataItemDefinition
                        {
                            Code = "temperature",
                            DisplayName = "温度",
                            DataType = "double",
                            Unit = "°C"
                        },
                        new ProcessDataItemDefinition
                        {
                            Code = "process.stage_number",
                            DisplayName = "阶段号",
                            DataType = "integer",
                            Category = "stage"
                        },
                        new ProcessDataItemDefinition
                        {
                            Code = "heater_enabled",
                            DisplayName = "加热",
                            DataType = "boolean",
                            Category = "state"
                        },
                        new ProcessDataItemDefinition
                        {
                            Code = "mode",
                            DisplayName = "模式",
                            DataType = "string",
                            Category = "state"
                        }
                    ]
                }
            },
            Plan = new ProcessAnalysisPlan
            {
                PlanId = "heat-treatment-execution",
                Version = 1,
                Name = "周期分析",
                Status = ConfigurationStatuses.Published,
                DataModelId = "heat-treatment",
                DataModelVersion = 2
            }
        };
}
