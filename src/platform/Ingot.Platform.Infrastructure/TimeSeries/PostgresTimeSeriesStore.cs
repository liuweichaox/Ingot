using System.Text.Json;
using Ingot.Domain.Events;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.TimeSeries;

/// <summary>
/// TimescaleDB implementation of the canonical signal model. Typed rows are the sole
/// persisted source for process samples; lifecycle and business events remain immutable
/// in production_events.
/// </summary>
public sealed class PostgresTimeSeriesStore : ITimeSeriesStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] StaticTagKeys =
    [
        "tenant_id",
        "factory_id",
        "plant_id",
        "line_id",
        "workcell_id",
        "equipment_id",
        "product_family_code"
    ];

    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresTimeSeriesStore(
        NpgsqlDataSource dataSource,
        ILogger<PostgresTimeSeriesStore> logger,
        IOptions<PlatformEventOptions> options)
    {
        _dataSource = dataSource;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;
        await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var topology = _dataSource.CreateCommand(
                """
                SELECT count(*) = 2
                FROM timescaledb_information.hypertables
                WHERE hypertable_schema = current_schema()
                  AND hypertable_name IN ('process_sample_frames', 'process_sample_values')
                """);
            if (await topology.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                throw new InvalidOperationException(
                    "标准测点 TimescaleDB 拓扑不存在；请先运行版本化数据库迁移。");
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }
    internal async Task<int> ProjectEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string siteId,
        string edgeId,
        long ingestId,
        DateTimeOffset ingestedAt,
        ProductionEvent evt,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var samples = TimeSeriesSampleProjector.Project(
            siteId, edgeId, ingestId, ingestedAt, evt, analysis);
        if (samples.Count == 0 || analysis is null)
            return 0;

        var projectedCodes = samples.Select(static sample => sample.SignalCode).ToHashSet(StringComparer.Ordinal);
        await UpsertSignalDefinitionsAsync(
            connection,
            transaction,
            analysis,
            analysis.DataModel.Acquisition.DataItems.Where(item => projectedCodes.Contains(item.Code)).ToArray(),
            evt.OccurredAt,
            ct).ConfigureAwait(false);

        await InsertFrameAsync(connection, transaction, samples[0], ct).ConfigureAwait(false);

        var pointKeys = await ResolveCollectionPointsAsync(
            connection, transaction, samples, evt.Context, ct).ConfigureAwait(false);
        await InsertValuesAsync(connection, samples, pointKeys, ct).ConfigureAwait(false);
        return samples.Count;
    }

    public async Task<IReadOnlyList<SignalSample>> QueryAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);
        var limit = Math.Clamp(query.Limit, 1, 100_000);
        var filters = new List<string>();
        await using var command = _dataSource.CreateCommand();
        if (!string.IsNullOrWhiteSpace(query.SiteId))
        {
            filters.Add("frame.site_id = @site_id");
            command.Parameters.AddWithValue("site_id", query.SiteId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.CollectionPointId))
        {
            filters.Add("point.collection_point_id = @collection_point_id");
            command.Parameters.AddWithValue("collection_point_id", query.CollectionPointId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SignalCode))
        {
            filters.Add("point.signal_code = @signal_code");
            command.Parameters.AddWithValue("signal_code", query.SignalCode.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.ExecutionId))
        {
            filters.Add("frame.execution_id = @execution_id");
            command.Parameters.AddWithValue("execution_id", query.ExecutionId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SubjectType))
        {
            filters.Add("frame.subject_type = @subject_type");
            command.Parameters.AddWithValue("subject_type", query.SubjectType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SubjectId))
        {
            filters.Add("frame.subject_id = @subject_id");
            command.Parameters.AddWithValue("subject_id", query.SubjectId.Trim());
        }
        if (query.From.HasValue)
        {
            filters.Add("frame.occurred_at >= @from");
            command.Parameters.AddWithValue("from", query.From.Value.UtcDateTime);
        }
        if (query.To.HasValue)
        {
            filters.Add("frame.occurred_at < @to");
            command.Parameters.AddWithValue("to", query.To.Value.UtcDateTime);
        }
        command.Parameters.AddWithValue("limit", limit);
        var where = filters.Count == 0 ? "" : $"WHERE {string.Join(" AND ", filters)}";
        command.CommandText =
            $"""
             SELECT point.collection_point_id, point.signal_code,
                    definition.data_type, definition.unit, definition.category,
                    frame.occurred_at, frame.recorded_at, frame.ingested_at,
                    frame.event_id, frame.frame_id, frame.site_id, frame.edge_id, frame.source,
                    frame.subject_type, frame.subject_id, frame.execution_id, frame.phase_code,
                    frame.data_model_id, frame.data_model_version, value.quality_code,
                    value.numeric_value, value.integer_value, value.boolean_value, value.text_value,
                    (SELECT ingest_key.seq FROM event_ingest_keys AS ingest_key
                     WHERE ingest_key.event_id = frame.event_id) AS source_sequence
             FROM process_sample_values AS value
             JOIN process_sample_frames AS frame
               ON frame.frame_id = value.frame_id AND frame.occurred_at = value.occurred_at
             JOIN collection_points AS point
               ON point.point_key = value.point_key
             JOIN signal_definitions AS definition
               ON definition.data_model_id = frame.data_model_id
              AND definition.data_model_version = frame.data_model_version
              AND definition.signal_code = point.signal_code
             {where}
             ORDER BY frame.occurred_at, frame.frame_id, point.signal_code
             LIMIT @limit;
             """;
        var result = new List<SignalSample>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new SignalSample
            {
                CollectionPointId = reader.GetString(0),
                SignalCode = reader.GetString(1),
                DataType = reader.GetString(2),
                Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                Category = reader.GetString(4),
                OccurredAt = new DateTimeOffset(reader.GetDateTime(5)),
                RecordedAt = new DateTimeOffset(reader.GetDateTime(6)),
                IngestedAt = reader.IsDBNull(7) ? null : new DateTimeOffset(reader.GetDateTime(7)),
                EventId = reader.GetString(8),
                IngestId = reader.GetInt64(9),
                SourceSequence = reader.IsDBNull(24) ? null : reader.GetInt64(24),
                SiteId = reader.GetString(10),
                EdgeId = reader.GetString(11),
                Source = reader.GetString(12),
                SubjectType = reader.GetString(13),
                SubjectId = reader.GetString(14),
                ExecutionId = reader.IsDBNull(15) ? null : reader.GetString(15),
                PhaseCode = reader.IsDBNull(16) ? null : reader.GetString(16),
                DataModelId = reader.GetString(17),
                DataModelVersion = reader.GetInt32(18),
                QualityCode = QualityCode(reader.GetInt16(19)),
                NumericValue = reader.IsDBNull(20) ? null : reader.GetDouble(20),
                IntegerValue = reader.IsDBNull(21) ? null : reader.GetInt64(21),
                BooleanValue = reader.IsDBNull(22) ? null : reader.GetBoolean(22),
                TextValue = reader.IsDBNull(23) ? null : reader.GetString(23)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<ProcessSampleFrame>> QueryFramesAsync(
        TimeSeriesQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(ct).ConfigureAwait(false);
        var limit = Math.Clamp(query.Limit, 1, 100_000);
        var frameFilters = new List<string>();
        var valueFilters = new List<string>();
        await using var command = _dataSource.CreateCommand();
        if (!string.IsNullOrWhiteSpace(query.SiteId))
        {
            frameFilters.Add("frame.site_id = @site_id");
            command.Parameters.AddWithValue("site_id", query.SiteId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.ExecutionId))
        {
            frameFilters.Add("frame.execution_id = @execution_id");
            command.Parameters.AddWithValue("execution_id", query.ExecutionId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SubjectType))
        {
            frameFilters.Add("frame.subject_type = @subject_type");
            command.Parameters.AddWithValue("subject_type", query.SubjectType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SubjectId))
        {
            frameFilters.Add("frame.subject_id = @subject_id");
            command.Parameters.AddWithValue("subject_id", query.SubjectId.Trim());
        }
        if (query.From.HasValue)
        {
            frameFilters.Add("frame.occurred_at >= @from");
            command.Parameters.AddWithValue("from", query.From.Value.UtcDateTime);
        }
        if (query.To.HasValue)
        {
            frameFilters.Add("frame.occurred_at < @to");
            command.Parameters.AddWithValue("to", query.To.Value.UtcDateTime);
        }
        if (query.AfterOccurredAt.HasValue != query.AfterFrameId.HasValue)
            throw new ArgumentException("帧查询游标必须同时包含时间和帧编号。", nameof(query));
        if (query.AfterOccurredAt.HasValue)
        {
            frameFilters.Add("(frame.occurred_at, frame.frame_id) > (@after_occurred_at, @after_frame_id)");
            command.Parameters.AddWithValue("after_occurred_at", query.AfterOccurredAt.Value.UtcDateTime);
            command.Parameters.AddWithValue("after_frame_id", query.AfterFrameId!.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.CollectionPointId))
        {
            valueFilters.Add("point.collection_point_id = @collection_point_id");
            command.Parameters.AddWithValue("collection_point_id", query.CollectionPointId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(query.SignalCode))
        {
            valueFilters.Add("point.signal_code = @signal_code");
            command.Parameters.AddWithValue("signal_code", query.SignalCode.Trim());
        }
        var frameWhere = frameFilters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", frameFilters)}";
        var valueWhere = valueFilters.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", valueFilters)}";
        var valueExists = valueFilters.Count == 0
            ? string.Empty
            : $"{(frameFilters.Count == 0 ? "WHERE" : "AND")} EXISTS (SELECT 1 FROM process_sample_values AS value "
              + "JOIN collection_points AS point ON point.point_key = value.point_key "
              + $"WHERE value.frame_id = frame.frame_id {string.Concat(valueFilters.Select(filter => $" AND {filter}"))})";
        command.Parameters.AddWithValue("limit", limit);
        command.CommandText =
            $"""
             WITH selected_frames AS (
               SELECT frame.*
               FROM process_sample_frames AS frame
               {frameWhere}
               {valueExists}
               ORDER BY frame.occurred_at, frame.frame_id
               LIMIT @limit
             )
             SELECT frame.event_id, frame.frame_id,
                    (SELECT ingest_key.seq FROM event_ingest_keys AS ingest_key
                     WHERE ingest_key.event_id = frame.event_id) AS source_sequence,
                    frame.occurred_at, frame.recorded_at, frame.ingested_at, frame.phase_code,
                    point.signal_code, value.numeric_value, value.integer_value, value.boolean_value
             FROM selected_frames AS frame
             JOIN process_sample_values AS value
               ON value.frame_id = frame.frame_id AND value.occurred_at = frame.occurred_at
             JOIN collection_points AS point
               ON point.point_key = value.point_key
             {valueWhere}
             ORDER BY frame.occurred_at, frame.frame_id, point.signal_code;
             """;
        var rows = new List<(string EventId, long FrameId, long? Sequence, DateTimeOffset OccurredAt,
            DateTimeOffset RecordedAt, DateTimeOffset? IngestedAt, string? PhaseCode, string SignalCode, double? Value)>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            double? value = reader.IsDBNull(8)
                ? reader.IsDBNull(9)
                    ? reader.IsDBNull(10) ? null : reader.GetBoolean(10) ? 1d : 0d
                    : reader.GetInt64(9)
                : reader.GetDouble(8);
            rows.Add((
                reader.GetString(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                new DateTimeOffset(reader.GetDateTime(3)),
                new DateTimeOffset(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : new DateTimeOffset(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7),
                value));
        }
        return rows.GroupBy(static row => row.EventId, StringComparer.Ordinal)
            .Select(static group =>
            {
                var first = group.First();
                return new ProcessSampleFrame
                {
                    EventId = first.EventId,
                    IngestId = first.FrameId,
                    SourceSequence = first.Sequence,
                    OccurredAt = first.OccurredAt,
                    RecordedAt = first.RecordedAt,
                    IngestedAt = first.IngestedAt,
                    PhaseCode = first.PhaseCode,
                    NumericValues = group.Where(static row => row.Value.HasValue)
                        .ToDictionary(static row => row.SignalCode, static row => row.Value!.Value, StringComparer.Ordinal)
                };
            })
            .ToArray();
    }

    private static async Task UpsertSignalDefinitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ResolvedProcessAnalysis analysis,
        IReadOnlyList<Ingot.Contracts.ProcessConfiguration.ProcessDataItemDefinition> definitions,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            WITH input AS (
              SELECT *
              FROM unnest(
                @codes::text[], @display_names::text[], @data_types::text[],
                @units::text[], @categories::text[], @hashes::text[])
              AS row(signal_code, display_name, data_type, unit, category, definition_hash)
            )
            INSERT INTO signal_definitions (
              data_model_id, data_model_version, signal_code, source_field,
              data_type, unit, category, definition_hash, first_seen_at, last_seen_at)
            SELECT
              @model_id, @model_version, signal_code, display_name,
              data_type, NULLIF(unit, ''), category, definition_hash, @seen_at, @seen_at
            FROM input
            ON CONFLICT (data_model_id, data_model_version, signal_code)
            DO UPDATE SET last_seen_at = GREATEST(signal_definitions.last_seen_at, EXCLUDED.last_seen_at);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("model_id", analysis.DataModel.ModelId);
        command.Parameters.AddWithValue("model_version", analysis.DataModel.Version);
        command.Parameters.AddWithValue("codes", definitions.Select(static item => item.Code).ToArray());
        command.Parameters.AddWithValue("display_names", definitions.Select(static item => item.DisplayName).ToArray());
        command.Parameters.AddWithValue("data_types", definitions.Select(static item => item.DataType).ToArray());
        command.Parameters.AddWithValue("units", definitions.Select(static item => item.Unit ?? string.Empty).ToArray());
        command.Parameters.AddWithValue("categories", definitions.Select(static item => item.Category).ToArray());
        command.Parameters.AddWithValue("hashes", definitions.Select(item => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"{analysis.DataModel.ModelId}|{analysis.DataModel.Version}|{item.Code}|{item.DisplayName}|{item.DataType}|{item.Unit}|{item.Category}")))
            .ToLowerInvariant()).ToArray());
        command.Parameters.AddWithValue("seen_at", occurredAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyDictionary<string, long>> ResolveCollectionPointsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<SignalSample> samples,
        IReadOnlyDictionary<string, string> eventContext,
        CancellationToken ct)
    {
        var staticTags = eventContext
            .Where(pair => StaticTagKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            """
            WITH input AS (
              SELECT *
              FROM unnest(
                @point_ids::text[], @site_ids::text[], @edge_ids::text[], @subject_types::text[],
                @subject_ids::text[], @signal_codes::text[], @seen_ats::timestamptz[])
              AS row(point_id, site_id, edge_id, subject_type, subject_id, signal_code, seen_at)
            ),
            upserted AS (
              INSERT INTO collection_points (
                collection_point_id, site_id, edge_id, subject_type, subject_id, signal_code,
                static_tags, first_seen_at, last_seen_at)
              SELECT
                point_id, site_id, edge_id, subject_type, subject_id, signal_code,
                @static_tags, seen_at, seen_at
              FROM input
              ON CONFLICT (collection_point_id)
              DO UPDATE SET
                static_tags = collection_points.static_tags || EXCLUDED.static_tags,
                last_seen_at = GREATEST(collection_points.last_seen_at, EXCLUDED.last_seen_at)
              RETURNING point_key, collection_point_id
            )
            SELECT point_key, collection_point_id FROM upserted;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("point_ids", samples.Select(static sample => sample.CollectionPointId).ToArray());
        command.Parameters.AddWithValue("site_ids", samples.Select(static sample => sample.SiteId).ToArray());
        command.Parameters.AddWithValue("edge_ids", samples.Select(static sample => sample.EdgeId).ToArray());
        command.Parameters.AddWithValue("subject_types", samples.Select(static sample => sample.SubjectType).ToArray());
        command.Parameters.AddWithValue("subject_ids", samples.Select(static sample => sample.SubjectId).ToArray());
        command.Parameters.AddWithValue("signal_codes", samples.Select(static sample => sample.SignalCode).ToArray());
        command.Parameters.AddWithValue("seen_ats", samples.Select(static sample => sample.OccurredAt.UtcDateTime).ToArray());
        command.Parameters.AddWithValue(
            "static_tags",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(staticTags, JsonOptions));
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result[reader.GetString(1)] = reader.GetInt64(0);
        return result;
    }

    private static async Task InsertFrameAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SignalSample sample,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO process_sample_frames (
              occurred_at, frame_id, event_id, recorded_at, ingested_at,
              site_id, edge_id, source, subject_type, subject_id, execution_id,
              phase_code, data_model_id, data_model_version)
            VALUES (
              @occurred_at, @frame_id, @event_id, @recorded_at, @ingested_at,
              @site_id, @edge_id, @source, @subject_type, @subject_id, @execution_id,
              @phase_code, @model_id, @model_version)
            ON CONFLICT (event_id, occurred_at) DO NOTHING;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("occurred_at", sample.OccurredAt.UtcDateTime);
        command.Parameters.AddWithValue("frame_id", sample.IngestId);
        command.Parameters.AddWithValue("event_id", sample.EventId);
        command.Parameters.AddWithValue("recorded_at", sample.RecordedAt.UtcDateTime);
        command.Parameters.AddWithValue("ingested_at", sample.IngestedAt?.UtcDateTime ?? DateTime.UtcNow);
        command.Parameters.AddWithValue("site_id", sample.SiteId);
        command.Parameters.AddWithValue("edge_id", sample.EdgeId);
        command.Parameters.AddWithValue("source", sample.Source);
        command.Parameters.AddWithValue("subject_type", sample.SubjectType);
        command.Parameters.AddWithValue("subject_id", sample.SubjectId);
        command.Parameters.AddWithValue("execution_id", (object?)sample.ExecutionId ?? DBNull.Value);
        command.Parameters.AddWithValue("phase_code", (object?)sample.PhaseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("model_id", sample.DataModelId);
        command.Parameters.AddWithValue("model_version", sample.DataModelVersion);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InsertValuesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<SignalSample> samples,
        IReadOnlyDictionary<string, long> pointKeys,
        CancellationToken ct)
    {
        await using var writer = await connection.BeginBinaryImportAsync(
            """
            COPY process_sample_values (
              occurred_at, frame_id, point_key, quality_code,
              numeric_value, integer_value, boolean_value, text_value)
            FROM STDIN (FORMAT BINARY)
            """,
            ct).ConfigureAwait(false);
        foreach (var sample in samples)
        {
            await writer.StartRowAsync(ct).ConfigureAwait(false);
            await writer.WriteAsync(sample.OccurredAt.UtcDateTime, NpgsqlDbType.TimestampTz, ct).ConfigureAwait(false);
            await writer.WriteAsync(sample.IngestId, NpgsqlDbType.Bigint, ct).ConfigureAwait(false);
            await writer.WriteAsync(pointKeys[sample.CollectionPointId], NpgsqlDbType.Bigint, ct).ConfigureAwait(false);
            await writer.WriteAsync(QualityCode(sample.QualityCode), NpgsqlDbType.Smallint, ct).ConfigureAwait(false);
            await WriteNullableAsync(writer, sample.NumericValue, NpgsqlDbType.Double, ct).ConfigureAwait(false);
            await WriteNullableAsync(writer, sample.IntegerValue, NpgsqlDbType.Bigint, ct).ConfigureAwait(false);
            await WriteNullableAsync(writer, sample.BooleanValue, NpgsqlDbType.Boolean, ct).ConfigureAwait(false);
            await WriteNullableAsync(writer, sample.TextValue, NpgsqlDbType.Text, ct).ConfigureAwait(false);
        }
        await writer.CompleteAsync(ct).ConfigureAwait(false);
    }

    private static async Task WriteNullableAsync<T>(
        NpgsqlBinaryImporter writer,
        T? value,
        NpgsqlDbType type,
        CancellationToken ct)
    {
        if (value is null)
            await writer.WriteNullAsync(ct).ConfigureAwait(false);
        else
            await writer.WriteAsync(value, type, ct).ConfigureAwait(false);
    }

    private static short QualityCode(string value)
        => value switch
        {
            SignalQualityCodes.Good => 0,
            SignalQualityCodes.Uncertain => 1,
            SignalQualityCodes.Bad => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "未知信号质量码。")
        };

    private static string QualityCode(short value)
        => value switch
        {
            0 => SignalQualityCodes.Good,
            1 => SignalQualityCodes.Uncertain,
            2 => SignalQualityCodes.Bad,
            _ => throw new InvalidDataException($"数据库包含未知信号质量码 {value}。")
        };

    public void Dispose() => _initializeLock.Dispose();
}
