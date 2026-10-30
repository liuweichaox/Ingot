// 为采集样本和源身份去重生成统一的质量证据。
using System.Globalization;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public static class AcquisitionSampleMetadata
{
    public static Dictionary<string, object?> CreateQuality(
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? valueFlags = null)
    {
        var missing = values.Count(item => item.Value is null);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var perValueFlags = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            var itemFlags = new HashSet<string>(StringComparer.Ordinal);
            if (item.Value is null)
                itemFlags.Add("missing_value");
            if (valueFlags is not null && valueFlags.TryGetValue(item.Key, out var configuredFlags))
                itemFlags.UnionWith(configuredFlags);
            if (itemFlags.Count == 0)
                continue;
            var ordered = itemFlags.Order(StringComparer.Ordinal).ToArray();
            perValueFlags[item.Key] = ordered;
            flags.UnionWith(ordered);
        }
        if (valueFlags is not null)
        {
            foreach (var itemFlags in valueFlags.Values)
                flags.UnionWith(itemFlags);
        }
        var status = missing > 0 ? "partial" : flags.Count > 0 ? "degraded" : "good";
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["quality"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["qualityVersion"] = 1,
                ["status"] = status,
                ["missingValueCount"] = missing,
                ["degradedValueCount"] = perValueFlags.Count,
                ["flags"] = flags.Order(StringComparer.Ordinal).ToArray(),
                ["valueFlags"] = perValueFlags,
                ["receivedAt"] = receivedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            }
        };
    }

    public static bool TryGetSourceIdentity(ProductionEvent sample, out string identity)
    {
        if (TryGetValue(sample, "sourceSequence", out var sequence))
        {
            identity = $"{sample.Source}|{sample.Subject.Type}/{sample.Subject.Id}|sequence:{sequence}";
            return true;
        }

        if (TryGetValue(sample, "sourceTimestamp", out var timestamp))
        {
            identity = $"{sample.Source}|{sample.Subject.Type}/{sample.Subject.Id}|timestamp:{timestamp}";
            return true;
        }

        identity = string.Empty;
        return false;
    }

    private static bool TryGetValue(
        ProductionEvent sample,
        string key,
        out string value)
    {
        if (sample.Data.TryGetValue(key, out var raw) && raw is not null)
        {
            value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }
}

public sealed class AcquisitionSourceDeduplicator
{
    private string? _lastIdentity;
    private DateTimeOffset? _lastIdentityChangedAt;

    public AcquisitionDeduplicationResult Evaluate(
        ProductionEvent sample,
        DateTimeOffset observedAt,
        TimeSpan staleAfter)
    {
        var result = EvaluatePending(sample, observedAt, staleAfter);
        if (result == AcquisitionDeduplicationResult.Changed)
            Commit(sample, observedAt);
        return result;
    }

    public AcquisitionDeduplicationResult EvaluatePending(
        ProductionEvent sample,
        DateTimeOffset observedAt,
        TimeSpan staleAfter)
    {
        if (!AcquisitionSampleMetadata.TryGetSourceIdentity(sample, out var identity))
            return AcquisitionDeduplicationResult.NoIdentity;
        if (string.Equals(_lastIdentity, identity, StringComparison.Ordinal))
        {
            var stale = staleAfter > TimeSpan.Zero &&
                        _lastIdentityChangedAt.HasValue &&
                        observedAt - _lastIdentityChangedAt.Value >= staleAfter;
            return stale
                ? AcquisitionDeduplicationResult.Stalled
                : AcquisitionDeduplicationResult.Duplicate;
        }
        return AcquisitionDeduplicationResult.Changed;
    }

    public void Commit(ProductionEvent sample, DateTimeOffset observedAt)
    {
        if (!AcquisitionSampleMetadata.TryGetSourceIdentity(sample, out var identity) ||
            string.Equals(_lastIdentity, identity, StringComparison.Ordinal))
            return;
        _lastIdentity = identity;
        _lastIdentityChangedAt = observedAt;
    }
}

public enum AcquisitionDeduplicationResult
{
    NoIdentity,
    Changed,
    Duplicate,
    Stalled
}
