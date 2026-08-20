// 实现边缘采集组件 IngestionTaskMaterializer，保持协议解析、凭据和领域事件边界分离。

using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public static class IngestionTaskMaterializer
{
    public static bool TryCreate(
        IngestionTaskTemplate? template,
        DataSourceInstance? dataSource,
        IngestionTaskBinding? binding,
        ProcessDataModel? model,
        out IngestionTask? task,
        out IReadOnlyList<AcquisitionValidationError> errors)
    {
        task = null;
        var found = new List<AcquisitionValidationError>();
        if (template is null)
            found.Add(new AcquisitionValidationError("template", "设备模板不能为空。"));
        if (dataSource is null)
            found.Add(new AcquisitionValidationError("dataSource", "数据源不能为空。"));
        if (binding is null)
            found.Add(new AcquisitionValidationError("binding", "任务绑定不能为空。"));
        if (found.Count > 0)
        {
            errors = found;
            return false;
        }

        if (template!.Status != ConfigurationStatuses.Published)
            found.Add(new AcquisitionValidationError("template.status", "只有已发布的任务模板可以实例化。"));
        if (dataSource!.Status != ConfigurationStatuses.Published)
            found.Add(new AcquisitionValidationError("dataSource.status", "只有已发布的数据源可以绑定任务。"));
        if (!string.Equals(template.Protocol, dataSource.Protocol, StringComparison.Ordinal))
            found.Add(new AcquisitionValidationError("dataSource.protocol", "数据源协议与任务模板协议不一致。"));
        if (!string.Equals(binding!.TemplateId, template.TemplateId, StringComparison.Ordinal) ||
            binding.TemplateVersion != template.Version)
            found.Add(new AcquisitionValidationError("binding.templateId", "任务绑定引用的模板版本不一致。"));
        if (!string.Equals(binding.DataSourceId, dataSource.DataSourceId, StringComparison.Ordinal) ||
            binding.DataSourceVersion != dataSource.Version)
            found.Add(new AcquisitionValidationError("binding.dataSourceId", "任务绑定引用的数据源版本不一致。"));
        if (model is not null &&
            (!string.Equals(model.ModelId, template.DataModelId, StringComparison.Ordinal) ||
             model.Version != template.DataModelVersion))
            found.Add(new AcquisitionValidationError("template.dataModelId", "任务模板引用的数据模型版本与实例化模型不一致。"));
        if (found.Count > 0)
        {
            errors = found;
            return false;
        }

        var candidate = new IngestionTask
        {
            TaskId = binding.TaskId,
            Version = binding.Version,
            Name = binding.Name,
            TemplateId = template.TemplateId,
            TemplateVersion = template.Version,
            DataSourceId = dataSource.DataSourceId,
            DataSourceVersion = dataSource.Version,
            Status = binding.Status,
            EdgeId = dataSource.EdgeId,
            Protocol = template.Protocol,
            DataModelId = template.DataModelId,
            DataModelVersion = template.DataModelVersion,
            Source = dataSource.SourceKey,
            SubjectType = dataSource.SubjectType,
            SubjectId = dataSource.SubjectId,
            HttpPolling = dataSource.HttpPolling ?? new HttpPollingConnection(),
            Mqtt = dataSource.Mqtt,
            OpcUa = dataSource.OpcUa,
            ModbusTcp = dataSource.ModbusTcp,
            MelsecA1E = dataSource.MelsecA1E,
            Execution = template.Execution,
            TimestampMode = template.TimestampMode,
            TimestampPath = template.TimestampPath,
            TimestampEncoding = template.TimestampEncoding,
            SequencePath = template.SequencePath,
            SampleEventType = template.SampleEventType,
            StaticContext = template.StaticContext,
            ContextMappings = ResolveTopics(template.ContextMappings, dataSource),
            ValueMappings = ResolveTopics(template.ValueMappings, dataSource),
            ProcessSpecification = ResolveTopics(template.ProcessSpecification, dataSource),
            Lifecycle = template.Lifecycle,
            UpdatedAt = binding.UpdatedAt
        };
        if (!IngestionTaskValidator.TryValidate(candidate, model, out task, out errors))
            return false;
        return true;
    }

    private static IReadOnlyList<AcquisitionContextMapping> ResolveTopics(
        IReadOnlyList<AcquisitionContextMapping> mappings,
        DataSourceInstance source)
        => mappings.Select(item => item with { Topic = ResolveTopic(item.Topic, source) }).ToArray();

    private static IReadOnlyList<AcquisitionValueMapping> ResolveTopics(
        IReadOnlyList<AcquisitionValueMapping> mappings,
        DataSourceInstance source)
        => mappings.Select(item => item with { Topic = ResolveTopic(item.Topic, source) }).ToArray();

    private static AcquisitionProcessSpecificationMapping? ResolveTopics(
        AcquisitionProcessSpecificationMapping? mapping,
        DataSourceInstance source)
        => mapping is null ? null : mapping with
        {
            ParameterMappings = ResolveTopics(mapping.ParameterMappings, source)
        };

    private static string? ResolveTopic(string? value, DataSourceInstance source)
    {
        if (string.IsNullOrWhiteSpace(value) || source.Protocol != AcquisitionProtocols.Mqtt)
            return value;
        var channels = source.Mqtt?.Topics
            .Where(static item => !string.IsNullOrWhiteSpace(item.Channel))
            .ToDictionary(static item => item.Channel!, static item => item.Topic, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return channels.GetValueOrDefault(value, value);
    }
}
