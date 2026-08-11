using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.TimeSeries;

public static class TimeSeriesSampleProjector
{
    public static IReadOnlyList<SignalSample> Project(
        string edgeId,
        long ingestId,
        ProductionEvent evt,
        ResolvedProcessAnalysis? analysis)
    {
        if (analysis is null ||
            !string.Equals(evt.EventType, "process.sample", StringComparison.Ordinal) ||
            !evt.Data.TryGetValue("values", out var rawValues) ||
            !TryReadObject(rawValues, out var values))
        {
            return [];
        }

        var quality = ReadQualityCodes(evt.Data);
        var phaseCode = ProcessAnalysisResolver.ResolveStage(evt.Context, evt.Data, analysis.DataModel);
        var definitions = analysis.DataModel.Acquisition.DataItems
            .ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var result = new List<SignalSample>(values.Count);
        foreach (var pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!definitions.TryGetValue(pair.Key, out var definition) ||
                IsNull(pair.Value))
            {
                continue;
            }

            var sample = CreateSample(
                edgeId,
                ingestId,
                evt,
                analysis.DataModel,
                definition,
                phaseCode,
                quality.GetValueOrDefault(definition.Code, SignalQualityCodes.Good),
                pair.Value);
            if (sample is not null)
                result.Add(sample);
        }
        return result;
    }

    public static string CollectionPointId(
        string edgeId,
        string subjectType,
        string subjectId,
        string signalCode)
        => string.Join(
            "/",
            NormalizeIdentity(edgeId),
            NormalizeIdentity(subjectType),
            NormalizeIdentity(subjectId),
            NormalizeIdentity(signalCode));

    private static SignalSample? CreateSample(
        string edgeId,
        long ingestId,
        ProductionEvent evt,
        ProcessDataModel model,
        ProcessDataItemDefinition definition,
        string? phaseCode,
        string qualityCode,
        object? raw)
    {
        double? numericValue = null;
        long? integerValue = null;
        bool? booleanValue = null;
        string? textValue = null;
        switch (definition.DataType)
        {
            case "integer":
                if (!TryReadInt64(raw, out var integer))
                    return null;
                integerValue = integer;
                break;
            case "boolean":
                if (!TryReadBoolean(raw, out var boolean))
                    return null;
                booleanValue = boolean;
                break;
            case "string":
                if (!TryReadString(raw, out var text))
                    return null;
                textValue = text;
                break;
            default:
                if (!TryReadDouble(raw, out var number))
                    return null;
                numericValue = number;
                break;
        }

        return new SignalSample
        {
            CollectionPointId = CollectionPointId(
                edgeId,
                evt.Subject.Type,
                evt.Subject.Id,
                definition.Code),
            SignalCode = definition.Code,
            DataType = definition.DataType,
            Unit = definition.Unit,
            Category = definition.Category,
            OccurredAt = evt.OccurredAt,
            RecordedAt = evt.RecordedAt,
            EventId = evt.EventId,
            IngestId = ingestId,
            EdgeId = edgeId,
            Source = evt.Source,
            SubjectType = evt.Subject.Type,
            SubjectId = evt.Subject.Id,
            ExecutionId = evt.ExecutionId,
            PhaseCode = phaseCode,
            DataModelId = model.ModelId,
            DataModelVersion = model.Version,
            QualityCode = NormalizeQuality(qualityCode),
            NumericValue = numericValue,
            IntegerValue = integerValue,
            BooleanValue = booleanValue,
            TextValue = textValue,
            RunContext = new Dictionary<string, string>(evt.Context, StringComparer.Ordinal)
        };
    }

    private static IReadOnlyDictionary<string, string> ReadQualityCodes(
        IReadOnlyDictionary<string, object?> data)
    {
        if (!data.TryGetValue("quality", out var raw) || !TryReadObject(raw, out var values))
            return new Dictionary<string, string>();
        return values
            .Where(static pair => TryReadString(pair.Value, out _))
            .ToDictionary(
                static pair => pair.Key,
                static pair =>
                {
                    TryReadString(pair.Value, out var value);
                    return NormalizeQuality(value);
                },
                StringComparer.Ordinal);
    }

    private static string NormalizeQuality(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            SignalQualityCodes.Bad => SignalQualityCodes.Bad,
            SignalQualityCodes.Uncertain => SignalQualityCodes.Uncertain,
            _ => SignalQualityCodes.Good
        };

    private static string NormalizeIdentity(string value)
        => Uri.EscapeDataString(value.Trim().ToLowerInvariant());

    private static bool TryReadObject(
        object? raw,
        out IReadOnlyDictionary<string, object?> values)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            values = element.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => (object?)property.Value,
                StringComparer.Ordinal);
            return true;
        }
        if (raw is IReadOnlyDictionary<string, object?> readOnly)
        {
            values = readOnly;
            return true;
        }
        if (raw is IDictionary<string, object?> dictionary)
        {
            values = dictionary.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            return true;
        }
        values = new Dictionary<string, object?>();
        return false;
    }

    private static bool IsNull(object? raw)
        => raw is null || raw is JsonElement { ValueKind: JsonValueKind.Null };

    private static bool TryReadDouble(object? raw, out double value)
    {
        if (raw is JsonElement element)
        {
            value = default;
            return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value);
        }
        value = default;
        try
        {
            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadInt64(object? raw, out long value)
    {
        if (raw is JsonElement element)
        {
            value = default;
            return element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value);
        }
        value = default;
        try
        {
            value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadBoolean(object? raw, out bool value)
    {
        if (raw is JsonElement element)
        {
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = element.GetBoolean();
                return true;
            }
            value = default;
            return false;
        }
        if (raw is bool boolean)
        {
            value = boolean;
            return true;
        }
        value = default;
        return false;
    }

    private static bool TryReadString(object? raw, out string value)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.String } element)
        {
            value = element.GetString() ?? "";
            return true;
        }
        if (raw is string text)
        {
            value = text;
            return true;
        }
        value = "";
        return false;
    }
}
