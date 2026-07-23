using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public static class JsonAcquisitionOptionsFactory
{
    public static HttpPollingAcquisitionOptions Create(AcquisitionDeployment deployment)
    {
        var profile = deployment.Profile;
        var dataItems = deployment.DataModel.Acquisition.DataItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var parameters = deployment.DataModel.RecipeParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var context = new Dictionary<string, string>(profile.StaticContext, StringComparer.Ordinal)
        {
            ["acquisition_profile_id"] = profile.ProfileId,
            ["acquisition_profile_version"] = profile.Version.ToString(),
            ["data_model_id"] = profile.DataModelId,
            ["data_model_version"] = profile.DataModelVersion.ToString()
        };
        return new HttpPollingAcquisitionOptions
        {
            Enabled = true,
            DeviceBaseUrl = profile.Connection.BaseUrl,
            SnapshotPath = profile.Connection.SnapshotPath,
            PollIntervalMs = profile.Connection.PollIntervalMs,
            SamplePeriodMs = deployment.DataModel.Acquisition.SamplePeriodMs,
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
                Required = item.Required
            }).ToArray(),
            Fields = profile.ValueMappings.Select(item => new ValueFieldMapping
            {
                Code = item.DataItemCode,
                SourcePath = item.SourcePath,
                DataType = dataItems[item.DataItemCode].DataType,
                Required = item.Required,
                Scale = item.Scale,
                Offset = item.Offset
            }).ToArray(),
            Recipe = profile.Recipe is null ? null : new RecipeFieldMapping
            {
                EventType = profile.Recipe.EventType,
                IdPath = profile.Recipe.IdPath,
                VersionPath = profile.Recipe.VersionPath,
                NamePath = profile.Recipe.NamePath,
                ParametersPath = profile.Recipe.ParametersPath,
                ParameterFields = profile.Recipe.ParameterMappings.Select(item => new ValueFieldMapping
                {
                    Code = item.DataItemCode,
                    SourcePath = item.SourcePath,
                    DataType = parameters[item.DataItemCode].DataType,
                    Required = item.Required,
                    Scale = item.Scale,
                    Offset = item.Offset
                }).ToArray()
            },
            Lifecycle = profile.Lifecycle is null ? null : new LifecycleFieldMapping
            {
                Mode = profile.Lifecycle.Mode,
                CorrelationIdContextKey = profile.Lifecycle.CorrelationIdContextKey,
                StepContextKey = profile.Lifecycle.StepContextKey,
                StepNameContextKey = profile.Lifecycle.StepNameContextKey,
                StartedEventType = profile.Lifecycle.StartedEventType,
                CompletedEventType = profile.Lifecycle.CompletedEventType,
                StepChangedEventType = profile.Lifecycle.StepChangedEventType,
                ExpectedDurationMs = profile.Lifecycle.ExpectedDurationMs
            }
        };
    }
}
