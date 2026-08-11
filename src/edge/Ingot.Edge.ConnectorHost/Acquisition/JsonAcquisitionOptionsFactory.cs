using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public static class JsonAcquisitionOptionsFactory
{
    public static HttpPollingAcquisitionOptions Create(AcquisitionDeployment deployment)
    {
        var profile = deployment.Profile;
        var dataItems = deployment.DataModel.Acquisition.DataItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var parameters = deployment.DataModel.ControlParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var context = new Dictionary<string, string>(profile.StaticContext, StringComparer.Ordinal)
        {
            ["acquisition_profile_id"] = profile.ProfileId,
            ["acquisition_profile_version"] = profile.Version.ToString(),
            ["data_model_id"] = profile.DataModelId,
            ["data_model_version"] = profile.DataModelVersion.ToString()
        };
        if (string.Equals(profile.SubjectType, "equipment", StringComparison.OrdinalIgnoreCase))
            context["equipment_id"] = profile.SubjectId;
        return new HttpPollingAcquisitionOptions
        {
            Enabled = true,
            DeviceBaseUrl = profile.Connection.BaseUrl,
            SnapshotPath = profile.Connection.SnapshotPath,
            PollIntervalMs = profile.Connection.PollIntervalMs,
            TimeoutMs = profile.Execution.TimeoutMs,
            Source = profile.Source,
            SubjectType = profile.SubjectType,
            SubjectId = profile.SubjectId,
            TimestampMode = profile.TimestampMode,
            TimestampPath = profile.TimestampPath,
            SequencePath = profile.SequencePath,
            SampleEventType = profile.SampleEventType,
            StaticContext = context,
            ContextFields = profile.ContextMappings.Select(item => new ContextFieldMapping
            {
                Key = item.ContextKey,
                SourcePath = item.SourcePath,
                Required = item.Required,
                Topic = item.Topic
            }).ToArray(),
            Fields = profile.ValueMappings.Select(item => new ValueFieldMapping
            {
                Code = item.DataItemCode,
                SourcePath = item.SourcePath,
                DataType = dataItems[item.DataItemCode].DataType,
                Category = dataItems[item.DataItemCode].Category,
                Required = item.Required,
                Scale = item.Scale,
                Offset = item.Offset,
                Topic = item.Topic
            }).ToArray(),
            ProcessSpecification = profile.ProcessSpecification is null ? null : new ProcessSpecificationFieldMapping
            {
                EventType = profile.ProcessSpecification.EventType,
                IdPath = profile.ProcessSpecification.IdPath,
                VersionPath = profile.ProcessSpecification.VersionPath,
                NamePath = profile.ProcessSpecification.NamePath,
                ParametersPath = profile.ProcessSpecification.ParametersPath,
                ParameterFields = profile.ProcessSpecification.ParameterMappings.Select(item => new ValueFieldMapping
                {
                    Code = item.DataItemCode,
                    SourcePath = item.SourcePath,
                    DataType = parameters[item.DataItemCode].DataType,
                    Required = item.Required,
                    Scale = item.Scale,
                    Offset = item.Offset,
                    Topic = item.Topic
                }).ToArray()
            },
            Lifecycle = profile.Lifecycle is null ? null : new LifecycleFieldMapping
            {
                Mode = profile.Lifecycle.Mode,
                ActiveContextKey = profile.Lifecycle.ActiveContextKey,
                ActiveValue = profile.Lifecycle.ActiveValue,
                StartedEventType = profile.Lifecycle.StartedEventType,
                CompletedEventType = profile.Lifecycle.CompletedEventType,
                StepChangedEventType = profile.Lifecycle.StepChangedEventType
            }
        };
    }
}
