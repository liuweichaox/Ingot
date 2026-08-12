using System.Globalization;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
/// 采样侧的可审计元数据和源级幂等判断。
/// </summary>
public static class AcquisitionSampleMetadata
{
    public static Dictionary<string, object?> CreateQuality(
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset receivedAt)
    {
        var missing = values.Count(item => item.Value is null);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["quality"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = missing == 0 ? "good" : "partial",
                ["missingValueCount"] = missing,
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
        _lastIdentity = identity;
        _lastIdentityChangedAt = observedAt;
        return AcquisitionDeduplicationResult.Changed;
    }
}

public enum AcquisitionDeduplicationResult
{
    NoIdentity,
    Changed,
    Duplicate,
    Stalled
}
