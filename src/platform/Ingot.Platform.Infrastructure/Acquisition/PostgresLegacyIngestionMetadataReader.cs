// Reads bounded, site-authorized metadata from archived ingestion tasks for historical evidence checks.
using Ingot.Platform.Application.Acquisition;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Acquisition;

public sealed class PostgresLegacyIngestionMetadataReader(NpgsqlDataSource dataSource)
    : ILegacyIngestionMetadataReader
{
    private const int MaximumVersionMetadataReferences = 40_000;
    private const int MaximumCoverageMetadataReferences = 2_000;
    private const int MaximumRequiredCoverageCodes = 500;

    public async Task<IReadOnlyList<IngestionTaskVersionMetadata>> FindVersionMetadataAsync(
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(authorizedEdgeIds);
        var scopedEdgeIds = NormalizeEdgeIds(authorizedEdgeIds);
        if (scopedEdgeIds.Length == 0)
            return [];

        var uniqueReferences = NormalizeReferences(references);
        if (uniqueReferences.Length > MaximumVersionMetadataReferences)
            throw new ArgumentOutOfRangeException(nameof(references),
                $"来源版本核验最多接受 {MaximumVersionMetadataReferences} 个唯一引用。");
        if (uniqueReferences.Length == 0)
            return [];

        await using var command = dataSource.CreateCommand(
            """
            SELECT requested.task_id, requested.version,
                   authorized.edge_id, authorized.status,
                   EXISTS (
                     SELECT 1
                     FROM ingot_legacy.ingestion_tasks AS candidate
                     WHERE candidate.task_id = requested.task_id
                       AND candidate.version = requested.version
                       AND NOT (candidate.edge_id = ANY(@authorized_edge_ids::text[]))
                   ) AS exists_outside_authorized_site
            FROM unnest(@task_ids::text[], @versions::integer[]) AS requested(task_id, version)
            LEFT JOIN ingot_legacy.ingestion_tasks AS authorized
              ON authorized.task_id = requested.task_id
             AND authorized.version = requested.version
             AND authorized.edge_id = ANY(@authorized_edge_ids::text[])
            ORDER BY requested.task_id, requested.version;
            """);
        AddReferenceParameters(command, uniqueReferences, scopedEdgeIds);

        var result = new List<IngestionTaskVersionMetadata>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new IngestionTaskVersionMetadata(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4)));
        }
        return result;
    }

    public async Task<IReadOnlyList<IngestionTaskCoverageMetadata>> FindCoverageMetadataAsync(
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(authorizedEdgeIds);
        var scopedEdgeIds = NormalizeEdgeIds(authorizedEdgeIds);
        if (scopedEdgeIds.Length == 0)
            return [];

        var uniqueReferences = NormalizeReferences(references);
        if (uniqueReferences.Length > MaximumCoverageMetadataReferences)
            throw new ArgumentOutOfRangeException(nameof(references),
                $"采集任务覆盖核验最多接受 {MaximumCoverageMetadataReferences} 个唯一版本引用。");
        if (uniqueReferences.Length == 0)
            return [];

        await using var command = dataSource.CreateCommand(
            """
            SELECT requested.task_id, requested.version,
                   authorized.edge_id, authorized.status,
                   COALESCE(authorized.payload ->> 'subjectType', 'equipment'),
                   authorized.payload ->> 'subjectId',
                   COALESCE(mapping_codes.required_codes, ARRAY[]::text[]),
                   COALESCE(mapping_codes.codes_truncated, false),
                   EXISTS (
                     SELECT 1
                     FROM ingot_legacy.ingestion_tasks AS candidate
                     WHERE candidate.task_id = requested.task_id
                       AND candidate.version = requested.version
                       AND NOT (candidate.edge_id = ANY(@authorized_edge_ids::text[]))
                   ) AS exists_outside_authorized_site
            FROM unnest(@task_ids::text[], @versions::integer[]) AS requested(task_id, version)
            LEFT JOIN ingot_legacy.ingestion_tasks AS authorized
              ON authorized.task_id = requested.task_id
             AND authorized.version = requested.version
             AND authorized.edge_id = ANY(@authorized_edge_ids::text[])
            LEFT JOIN LATERAL (
              SELECT array_agg(bounded.code ORDER BY bounded.code) AS required_codes,
                     count(*) > @maximum_required_codes AS codes_truncated
              FROM (
                SELECT DISTINCT btrim(mapping.value ->> 'dataItemCode') AS code
                FROM jsonb_array_elements(
                  CASE
                    WHEN jsonb_typeof(authorized.payload -> 'valueMappings') = 'array'
                      THEN authorized.payload -> 'valueMappings'
                    ELSE '[]'::jsonb
                  END) AS mapping(value)
                WHERE COALESCE(mapping.value ->> 'required', 'true') = 'true'
                  AND NULLIF(btrim(mapping.value ->> 'dataItemCode'), '') IS NOT NULL
                ORDER BY code
                LIMIT @maximum_required_codes_plus_one
              ) AS bounded
            ) AS mapping_codes ON authorized.task_id IS NOT NULL
            ORDER BY requested.task_id, requested.version;
            """);
        AddReferenceParameters(command, uniqueReferences, scopedEdgeIds);
        command.Parameters.AddWithValue("maximum_required_codes", MaximumRequiredCoverageCodes);
        command.Parameters.AddWithValue("maximum_required_codes_plus_one", MaximumRequiredCoverageCodes + 1);

        var result = new List<IngestionTaskCoverageMetadata>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var requiredCodes = reader.GetFieldValue<string[]>(6);
            var requiredCodesTruncated = reader.GetBoolean(7);
            if (requiredCodesTruncated)
                requiredCodes = requiredCodes.Take(MaximumRequiredCoverageCodes).ToArray();
            result.Add(new IngestionTaskCoverageMetadata(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                requiredCodes,
                reader.GetBoolean(8),
                requiredCodesTruncated));
        }
        return result;
    }

    private static string[] NormalizeEdgeIds(IEnumerable<string> edgeIds)
        => edgeIds
            .Where(static edgeId => !string.IsNullOrWhiteSpace(edgeId))
            .Select(static edgeId => edgeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IngestionTaskVersionReference[] NormalizeReferences(
        IEnumerable<IngestionTaskVersionReference> references)
        => references
            .Where(static item => item.Version > 0 && !string.IsNullOrWhiteSpace(item.TaskId))
            .Select(static item => new IngestionTaskVersionReference(
                item.TaskId.Trim().ToLowerInvariant(), item.Version))
            .Distinct()
            .ToArray();

    private static void AddReferenceParameters(
        NpgsqlCommand command,
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds)
    {
        command.Parameters.Add("task_ids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
            references.Select(static item => item.TaskId).ToArray();
        command.Parameters.Add("versions", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value =
            references.Select(static item => item.Version).ToArray();
        command.Parameters.Add("authorized_edge_ids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value =
            authorizedEdgeIds.ToArray();
    }
}
