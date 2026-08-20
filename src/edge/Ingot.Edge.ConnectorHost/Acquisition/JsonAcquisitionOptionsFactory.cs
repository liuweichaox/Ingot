// 实现边缘采集组件 JsonAcquisitionOptionsFactory，保持协议解析、凭据和领域事件边界分离。

using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public static class JsonAcquisitionOptionsFactory
{
    public static HttpPollingAcquisitionOptions Create(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        var dataItems = deployment.DataModel.Acquisition.DataItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var parameters = deployment.DataModel.ControlParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var context = new Dictionary<string, string>(task.StaticContext, StringComparer.Ordinal)
        {
            ["ingestion_task_id"] = task.TaskId,
            ["ingestion_task_version"] = task.Version.ToString(),
            ["data_model_id"] = task.DataModelId,
            ["data_model_version"] = task.DataModelVersion.ToString()
        };
        if (string.Equals(task.SubjectType, "equipment", StringComparison.OrdinalIgnoreCase))
            context["equipment_id"] = task.SubjectId;
        return new HttpPollingAcquisitionOptions
        {
            ConfigurationKind = "ingestion-task",
            ConfigurationId = task.TaskId,
            ConfigurationVersion = task.Version,
            Enabled = true,
            DeviceBaseUrl = task.HttpPolling.BaseUrl,
            SnapshotPath = task.HttpPolling.SnapshotPath,
            Method = task.HttpPolling.Method,
            ContentType = task.HttpPolling.ContentType,
            RequestBody = task.HttpPolling.RequestBody,
            Headers = task.HttpPolling.Headers,
            HeaderSecretRefs = task.HttpPolling.HeaderSecretRefs,
            PollIntervalMs = task.HttpPolling.PollIntervalMs,
            TimeoutMs = task.Execution.TimeoutMs,
            ReconnectDelayMs = task.Execution.ReconnectDelayMs,
            SourceIdentityStaleAfterMs = task.Execution.SourceIdentityStaleAfterMs,
            MaximumFutureTimestampSkewMs = task.Execution.MaximumFutureTimestampSkewMs,
            Source = task.Source,
            SubjectType = task.SubjectType,
            SubjectId = task.SubjectId,
            TimestampMode = task.TimestampMode,
            TimestampEncoding = task.TimestampEncoding,
            TimestampPath = task.TimestampPath,
            SequencePath = task.SequencePath,
            SampleEventType = task.SampleEventType,
            StaticContext = context,
            ContextFields = task.ContextMappings.Select(item => new ContextFieldMapping
            {
                Key = item.ContextKey,
                SourcePath = item.SourcePath,
                Required = item.Required,
                Topic = item.Topic
            }).ToArray(),
            Fields = task.ValueMappings.Select(item => new ValueFieldMapping
            {
                Code = item.DataItemCode,
                SourcePath = item.SourcePath,
                DataType = dataItems[item.DataItemCode].DataType,
                Category = dataItems[item.DataItemCode].Category,
                Required = item.Required,
                Scale = item.Scale,
                Offset = item.Offset,
                QualityPath = item.QualityPath,
                AcceptedQualityValues = item.AcceptedQualityValues,
                Minimum = item.Minimum,
                Maximum = item.Maximum,
                OutOfRangeBehavior = item.OutOfRangeBehavior,
                MissingValueBehavior = item.MissingValueBehavior,
                DefaultValue = item.DefaultValue,
                Topic = item.Topic
            }).ToArray(),
            ProcessSpecification = task.ProcessSpecification is null ? null : new ProcessSpecificationFieldMapping
            {
                EventType = task.ProcessSpecification.EventType,
                IdPath = task.ProcessSpecification.IdPath,
                VersionPath = task.ProcessSpecification.VersionPath,
                NamePath = task.ProcessSpecification.NamePath,
                ParametersPath = task.ProcessSpecification.ParametersPath,
                ParameterFields = task.ProcessSpecification.ParameterMappings.Select(item => new ValueFieldMapping
                {
                    Code = item.DataItemCode,
                    SourcePath = item.SourcePath,
                    DataType = parameters[item.DataItemCode].DataType,
                    Required = item.Required,
                    Scale = item.Scale,
                    Offset = item.Offset,
                    QualityPath = item.QualityPath,
                    AcceptedQualityValues = item.AcceptedQualityValues,
                    Minimum = item.Minimum,
                    Maximum = item.Maximum,
                    OutOfRangeBehavior = item.OutOfRangeBehavior,
                    MissingValueBehavior = item.MissingValueBehavior,
                    DefaultValue = item.DefaultValue,
                    Topic = item.Topic
                }).ToArray()
            },
            Lifecycle = task.Lifecycle is null ? null : new LifecycleFieldMapping
            {
                Mode = task.Lifecycle.Mode,
                ActiveContextKey = task.Lifecycle.ActiveContextKey,
                ActiveValue = task.Lifecycle.ActiveValue,
                StartedEventType = task.Lifecycle.StartedEventType,
                CompletedEventType = task.Lifecycle.CompletedEventType,
                StepChangedEventType = task.Lifecycle.StepChangedEventType
            }
        };
    }
}
