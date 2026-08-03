using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ingot.Contracts.Acquisition;

public static class AcquisitionConfigurationSources
{
    public const string Platform = "platform";
    public const string Cache = "cache";
    public const string Local = "local";
}

public static class AcquisitionApplicationStates
{
    public const string Pending = "pending";
    public const string Validating = "validating";
    public const string WaitingForCycleBoundary = "waiting-cycle-boundary";
    public const string Applying = "applying";
    public const string Applied = "applied";
    public const string Rollback = "rollback";
    public const string Failed = "failed";
}

public sealed record AcquisitionTaskRuntimeStatus(
    string ConfigurationKey,
    string State,
    DateTimeOffset LoadedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    long SamplesCollected,
    double? LastReadDurationMs,
    double? ObservedIntervalMs,
    string? ActiveRecipe,
    string? LastError,
    bool CycleActive,
    long StaleSnapshotRejectionCount = 0,
    long StaleValueRejectionCount = 0);

public sealed record AcquisitionDeploymentApplicationStatus(
    string ProfileId,
    int DesiredVersion,
    string DesiredConfigurationHash,
    int? AppliedVersion,
    string? AppliedConfigurationHash,
    string State,
    DateTimeOffset DesiredAt,
    DateTimeOffset? AppliedAt,
    string? LastError);

public sealed record EdgeAcquisitionRuntimeStatus(
    bool Enabled,
    string State,
    DateTimeOffset ReportedAt,
    string? ConfigurationSource,
    string? DesiredConfigurationSetHash,
    string? AppliedConfigurationSetHash,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    long SamplesCollected,
    double? LastReadDurationMs,
    double? ObservedIntervalMs,
    string? ActiveRecipe,
    string? LastError,
    IReadOnlyList<AcquisitionTaskRuntimeStatus> Tasks,
    IReadOnlyList<AcquisitionDeploymentApplicationStatus> Deployments,
    long StaleSnapshotRejectionCount = 0,
    long StaleValueRejectionCount = 0);

/// <summary>
///     Produces a stable SHA-256 fingerprint for immutable acquisition deployments.
///     Object properties are recursively sorted so dictionary insertion order does not alter identity.
/// </summary>
public static class AcquisitionDeploymentFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Compute(AcquisitionDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        var node = JsonSerializer.SerializeToNode(deployment, JsonOptions)
            ?? throw new InvalidOperationException("The acquisition deployment could not be serialized.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, node);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static string ComputeSet(IEnumerable<AcquisitionDeployment> deployments)
    {
        ArgumentNullException.ThrowIfNull(deployments);
        var identities = deployments
            .Select(item => $"{item.Profile.ProfileId}@{item.Profile.Version}:{Compute(item)}")
            .OrderBy(static item => item, StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join('\n', identities))));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject value:
                writer.WriteStartObject();
                foreach (var property in value.OrderBy(static item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray value:
                writer.WriteStartArray();
                foreach (var item in value)
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValue value:
                value.WriteTo(writer);
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON node: {node.GetType().Name}.");
        }
    }
}
