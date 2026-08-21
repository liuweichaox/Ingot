using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public sealed record ReusableIngestionConfiguration
{
    public required IngestionTaskTemplate Template { get; init; }
    public required DataSourceInstance DataSource { get; init; }
    public required IngestionTaskBinding Binding { get; init; }
    public required IngestionTask Task { get; init; }
}

public static class IngestionTaskDecomposer
{
    public static bool TryCreate(
        IngestionTask? task,
        ProcessDataModel? model,
        string? templateId,
        string? dataSourceId,
        out ReusableIngestionConfiguration? result,
        out IReadOnlyList<AcquisitionValidationError> errors)
        => TryCreate(task, model, templateId, 1, dataSourceId, 1, out result, out errors);

    public static bool TryCreate(
        IngestionTask? task,
        ProcessDataModel? model,
        string? templateId,
        int templateVersion,
        string? dataSourceId,
        int dataSourceVersion,
        out ReusableIngestionConfiguration? result,
        out IReadOnlyList<AcquisitionValidationError> errors)
    {
        result = null;
        if (task is null || model is null)
        {
            errors = [new AcquisitionValidationError(string.Empty, "任务和工艺数据模型不能为空。")];
            return false;
        }
        if (task.Status != ConfigurationStatuses.Published)
        {
            errors = [new AcquisitionValidationError("status", "只有已通过现场验证并发布的任务可以提取复用资产。")];
            return false;
        }
        if (!string.IsNullOrWhiteSpace(task.TemplateId) || !string.IsNullOrWhiteSpace(task.DataSourceId))
        {
            errors = [new AcquisitionValidationError(
                "templateId",
                "该任务已经具有模板和数据源版本来源；不能再次提取并改写来源，请创建新的资产版本和任务绑定。")];
            return false;
        }
        if (task.Version == int.MaxValue)
        {
            errors = [new AcquisitionValidationError("version", "当前任务版本已达到上限，无法创建带复用来源的新版本。")];
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var source = new DataSourceInstance
        {
            DataSourceId = dataSourceId ?? string.Empty,
            Version = dataSourceVersion,
            Name = $"{task.Name} 数据源",
            Status = ConfigurationStatuses.Published,
            EdgeId = task.EdgeId,
            Protocol = task.Protocol,
            SourceKey = task.Source,
            SubjectType = task.SubjectType,
            SubjectId = task.SubjectId,
            HttpPolling = task.Protocol == AcquisitionProtocols.HttpPolling ? task.HttpPolling : null,
            Mqtt = task.Mqtt,
            OpcUa = task.OpcUa,
            ModbusTcp = task.ModbusTcp,
            MelsecA1E = task.MelsecA1E,
            UpdatedAt = now
        };
        var template = new IngestionTaskTemplate
        {
            TemplateId = templateId ?? string.Empty,
            Version = templateVersion,
            Name = $"{task.Name} 接入模板",
            Status = ConfigurationStatuses.Published,
            Protocol = task.Protocol,
            DataModelId = task.DataModelId,
            DataModelVersion = task.DataModelVersion,
            Execution = task.Execution,
            TimestampMode = task.TimestampMode,
            TimestampPath = task.TimestampPath,
            TimestampEncoding = task.TimestampEncoding,
            SequencePath = task.SequencePath,
            SampleEventType = task.SampleEventType,
            StaticContext = task.StaticContext,
            ContextMappings = ReplaceMqttTopics(task.ContextMappings, source),
            ValueMappings = ReplaceMqttTopics(task.ValueMappings, source),
            ProcessSpecification = ReplaceMqttTopics(task.ProcessSpecification, source),
            Lifecycle = task.Lifecycle,
            UpdatedAt = now
        };
        var binding = new IngestionTaskBinding
        {
            TaskId = task.TaskId,

            Version = task.Version + 1,
            Name = task.Name,
            Status = ConfigurationStatuses.Published,
            TemplateId = template.TemplateId,
            TemplateVersion = template.Version,
            DataSourceId = source.DataSourceId,
            DataSourceVersion = source.Version,
            UpdatedAt = now
        };

        var found = new List<AcquisitionValidationError>();
        if (!IngestionTaskValidator.TryValidateTemplate(template, model, out var validTemplate, out var templateErrors))
            found.AddRange(templateErrors);
        if (!IngestionTaskValidator.TryValidateDataSource(source, out var validSource, out var sourceErrors))
            found.AddRange(sourceErrors);
        if (!IngestionTaskValidator.TryValidateBinding(binding, out var validBinding, out var bindingErrors))
            found.AddRange(bindingErrors);
        if (found.Count > 0)
        {
            errors = found;
            return false;
        }
        if (!IngestionTaskMaterializer.TryCreate(
                validTemplate, validSource, validBinding, model, out var materialized, out var materializeErrors))
        {
            errors = materializeErrors;
            return false;
        }

        result = new ReusableIngestionConfiguration
        {
            Template = validTemplate!,
            DataSource = validSource!,
            Binding = validBinding!,
            Task = materialized!
        };
        errors = [];
        return true;
    }

    private static IReadOnlyList<AcquisitionContextMapping> ReplaceMqttTopics(
        IReadOnlyList<AcquisitionContextMapping> mappings,
        DataSourceInstance source)
        => mappings.Select(item => item with { Topic = ChannelFor(item.Topic, source) }).ToArray();

    private static IReadOnlyList<AcquisitionValueMapping> ReplaceMqttTopics(
        IReadOnlyList<AcquisitionValueMapping> mappings,
        DataSourceInstance source)
        => mappings.Select(item => item with { Topic = ChannelFor(item.Topic, source) }).ToArray();

    private static AcquisitionProcessSpecificationMapping? ReplaceMqttTopics(
        AcquisitionProcessSpecificationMapping? mapping,
        DataSourceInstance source)
        => mapping is null ? null : mapping with
        {
            ParameterMappings = ReplaceMqttTopics(mapping.ParameterMappings, source)
        };

    private static string? ChannelFor(string? topic, DataSourceInstance source)
    {
        if (string.IsNullOrWhiteSpace(topic) || source.Protocol != AcquisitionProtocols.Mqtt)
            return topic;
        return source.Mqtt?.Topics.FirstOrDefault(item =>
            string.Equals(item.Topic, topic, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(item.Channel))?.Channel ?? topic;
    }
}
