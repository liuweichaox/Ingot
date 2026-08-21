using System.Globalization;
using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public static class ProtocolAcquisitionSnapshotMapper
{
    public static AcquisitionMappingResult Map(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> raw,
        string normalizedSource,
        string? previousProcessSpecificationIdentity,
        DateTimeOffset occurredAt)
    {
        var task = deployment.Task;
        var appliedConfiguration = new AppliedConfigurationRef(
            "ingestion-task",
            task.TaskId,
            task.Version);
        var dataItems = deployment.DataModel.Acquisition.DataItems
            .ToDictionary(item => item.Code, StringComparer.Ordinal);
        var context = new Dictionary<string, string>(task.StaticContext, StringComparer.Ordinal)
        {
            ["ingestion_task_id"] = task.TaskId,
            ["ingestion_task_version"] = task.Version.ToString(CultureInfo.InvariantCulture),
            ["data_model_id"] = task.DataModelId,
            ["data_model_version"] = task.DataModelVersion.ToString(CultureInfo.InvariantCulture)
        };
        if (string.Equals(task.SubjectType, "equipment", StringComparison.OrdinalIgnoreCase))
            context["equipment_id"] = task.SubjectId;
        foreach (var mapping in task.ContextMappings)
        {
            if (!raw.TryGetValue(mapping.SourcePath, out var value) || value is null)
            {
                if (mapping.Required)
                    throw new InvalidDataException($"采集源缺少必填上下文：{mapping.SourcePath}。");
                continue;
            }
            context[mapping.ContextKey] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var mapping in task.ValueMappings)
        {
            values[mapping.DataItemCode] = AcquisitionValuePolicy.Resolve(
                raw,
                mapping,
                dataItems[mapping.DataItemCode].DataType);
        }
        var stageDefinition = dataItems.Values.SingleOrDefault(item => item.Category == "stage");
        if (stageDefinition is not null &&
            values.TryGetValue(stageDefinition.Code, out var stageValue) &&
            stageValue is not null)
        {
            context["stage_number"] =
                Convert.ToString(stageValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        string? processSpecificationIdentity = null;
        ProductionEvent? processSpecificationApplied = null;
        if (task.ProcessSpecification is not null)
        {
            var processSpecification = task.ProcessSpecification;
            var processSpecificationId = RequiredScalar(raw, processSpecification.IdPath);
            var processSpecificationVersion = RequiredScalar(raw, processSpecification.VersionPath);
            processSpecificationIdentity = $"{processSpecificationId}@{processSpecificationVersion}";
            context["process_specification_id"] = processSpecificationId;
            context["process_specification_version"] = processSpecificationVersion;
            if (!string.Equals(processSpecificationIdentity, previousProcessSpecificationIdentity, StringComparison.Ordinal))
            {
                var definitions = deployment.DataModel.ControlParameters
                    .ToDictionary(item => item.Code, StringComparer.Ordinal);
                var resolved = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var mapping in processSpecification.ParameterMappings)
                {
                    resolved[mapping.DataItemCode] = AcquisitionValuePolicy.Resolve(
                        raw,
                        mapping,
                        definitions[mapping.DataItemCode].DataType);
                }
                var data = new Dictionary<string, object?>
                {
                    ["processSpecificationId"] = processSpecificationId,
                    ["processSpecificationVersion"] = ScalarValue(raw[processSpecification.VersionPath]!),
                    ["resolvedParameters"] = resolved
                };
                if (!string.IsNullOrWhiteSpace(processSpecification.NamePath) &&
                    raw.TryGetValue(processSpecification.NamePath, out var name) &&
                    name is not null)
                {
                    data["processSpecificationName"] = Convert.ToString(name, CultureInfo.InvariantCulture);
                }
                processSpecificationApplied = ProductionEvent.Create(
                    processSpecification.EventType,
                    occurredAt,
                    normalizedSource,
                    new ObjectRef(task.SubjectType, task.SubjectId),
                    executionId: null,
                    context,
                    data,
                    appliedConfiguration);
            }
        }

        var sampleData = AcquisitionSampleMetadata.CreateQuality(values, DateTimeOffset.UtcNow);
        sampleData["values"] = values;
        if (task.TimestampMode == "source")
            sampleData["sourceTimestamp"] = occurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        var sample = ProductionEvent.Create(
            task.SampleEventType,
            occurredAt,
            normalizedSource,
            new ObjectRef(task.SubjectType, task.SubjectId),
            executionId: null,
            context,
            sampleData,
            appliedConfiguration,
            QualityFlags(values));
        return new AcquisitionMappingResult(sample, processSpecificationApplied, processSpecificationIdentity);
    }

    private static string RequiredScalar(IReadOnlyDictionary<string, object?> raw, string sourcePath)
    {
        if (!raw.TryGetValue(sourcePath, out var value) || value is null)
            throw new InvalidDataException($"采集源缺少必填字段：{sourcePath}。");
        return Convert.ToString(value, CultureInfo.InvariantCulture)
               ?? throw new InvalidDataException($"采集字段不是标量：{sourcePath}。");
    }

    private static object ScalarValue(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : value;

    private static IReadOnlyList<string> QualityFlags(IReadOnlyDictionary<string, object?> values)
        => values.Any(static pair => pair.Value is null) ? ["missing_value"] : [];

}
