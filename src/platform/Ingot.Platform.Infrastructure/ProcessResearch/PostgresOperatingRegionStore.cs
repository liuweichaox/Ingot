using Ingot.Platform.Application.ProcessResearch;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public interface IOperatingRegionStore
{
    Task<ParameterBounds?> GetParameterBoundsAsync(
        string operatingRegionId,
        string parameterName,
        CancellationToken ct);

    Task<IReadOnlyList<ParameterBounds>> ListParameterBoundsAsync(
        string operatingRegionId,
        CancellationToken ct);

    Task SaveParameterBoundsAsync(
        ParameterBounds bounds,
        CancellationToken ct);

    Task<ValidationHistoryRecord?> GetValidationHistoryAsync(
        string validationHistoryId,
        CancellationToken ct);

    Task<IReadOnlyList<ValidationHistoryRecord>> QueryValidationHistoryAsync(
        string operatingRegionId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 1000,
        CancellationToken ct = default);

    Task SaveValidationHistoryAsync(
        ValidationHistoryRecord record,
        CancellationToken ct);

    Task<IReadOnlyList<OperatingRegionExtension>> QueryExtensionsAsync(
        string operatingRegionId,
        bool approvedOnly = false,
        CancellationToken ct = default);

    Task SaveExtensionAsync(
        OperatingRegionExtension extension,
        CancellationToken ct);

    Task ApproveExtensionAsync(
        string extensionId,
        string approvedBy,
        string? notes,
        CancellationToken ct);

    Task<ParameterConstraint?> GetConstraintAsync(
        string constraintId,
        CancellationToken ct);

    Task<IReadOnlyList<ParameterConstraint>> ListConstraintsAsync(
        string operatingRegionId,
        CancellationToken ct);

    Task SaveConstraintAsync(
        ParameterConstraint constraint,
        CancellationToken ct);
}

public sealed class PostgresOperatingRegionStore(NpgsqlDataSource dataSource) : IOperatingRegionStore
{
    public async Task<ParameterBounds?> GetParameterBoundsAsync(
        string operatingRegionId,
        string parameterName,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT parameter_bounds_id, operating_region_id, parameter_name, min_value, max_value,
                   unit_of_measure, criticality_level, correlation_notes, created_at, updated_at
            FROM research_operating_region_parameter_bounds
            WHERE operating_region_id = $1 AND parameter_name = $2
            LIMIT 1";
        cmd.Parameters.AddWithValue("$1", operatingRegionId);
        cmd.Parameters.AddWithValue("$2", parameterName);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new ParameterBounds(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt16(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9))
            : null;
    }

    public async Task<IReadOnlyList<ParameterBounds>> ListParameterBoundsAsync(
        string operatingRegionId,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT parameter_bounds_id, operating_region_id, parameter_name, min_value, max_value,
                   unit_of_measure, criticality_level, correlation_notes, created_at, updated_at
            FROM research_operating_region_parameter_bounds
            WHERE operating_region_id = $1
            ORDER BY criticality_level DESC, parameter_name";
        cmd.Parameters.AddWithValue("$1", operatingRegionId);

        var results = new List<ParameterBounds>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new ParameterBounds(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt16(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9)));

        return results;
    }

    public async Task SaveParameterBoundsAsync(
        ParameterBounds bounds,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO research_operating_region_parameter_bounds
            (parameter_bounds_id, operating_region_id, parameter_name, min_value, max_value,
             unit_of_measure, criticality_level, correlation_notes, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            ON CONFLICT (operating_region_id, parameter_name)
            DO UPDATE SET min_value = $4, max_value = $5, unit_of_measure = $6,
                          criticality_level = $7, correlation_notes = $8, updated_at = $10";

        cmd.Parameters.AddWithValue("$1", bounds.ParameterBoundsId);
        cmd.Parameters.AddWithValue("$2", bounds.OperatingRegionId);
        cmd.Parameters.AddWithValue("$3", bounds.ParameterName);
        cmd.Parameters.AddWithValue("$4", bounds.MinValue);
        cmd.Parameters.AddWithValue("$5", bounds.MaxValue);
        cmd.Parameters.AddWithValue("$6", bounds.UnitOfMeasure ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$7", bounds.CriticalityLevel);
        cmd.Parameters.AddWithValue("$8", bounds.CorrelationNotes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$9", bounds.CreatedAt);
        cmd.Parameters.AddWithValue("$10", bounds.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ValidationHistoryRecord?> GetValidationHistoryAsync(
        string validationHistoryId,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT validation_history_id, operating_region_id, experiment_id, execution_id,
                   validation_timestamp, parameter_values, outcome_status, quality_score, notes, created_at
            FROM research_operating_region_validation_history
            WHERE validation_history_id = $1
            LIMIT 1";
        cmd.Parameters.AddWithValue("$1", validationHistoryId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? MapValidationHistory(reader)
            : null;
    }

    public async Task<IReadOnlyList<ValidationHistoryRecord>> QueryValidationHistoryAsync(
        string operatingRegionId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 1000,
        CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT validation_history_id, operating_region_id, experiment_id, execution_id,
                   validation_timestamp, parameter_values, outcome_status, quality_score, notes, created_at
            FROM research_operating_region_validation_history
            WHERE operating_region_id = $1";

        var paramIndex = 1;
        cmd.Parameters.AddWithValue("$" + ++paramIndex, operatingRegionId);

        if (from.HasValue)
        {
            cmd.CommandText += $" AND validation_timestamp >= ${++paramIndex}";
            cmd.Parameters.AddWithValue("$" + paramIndex, from.Value);
        }

        if (to.HasValue)
        {
            cmd.CommandText += $" AND validation_timestamp <= ${++paramIndex}";
            cmd.Parameters.AddWithValue("$" + paramIndex, to.Value);
        }

        cmd.CommandText += $" ORDER BY validation_timestamp DESC LIMIT {limit}";

        var results = new List<ValidationHistoryRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapValidationHistory(reader));

        return results;
    }

    public async Task SaveValidationHistoryAsync(
        ValidationHistoryRecord record,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO research_operating_region_validation_history
            (validation_history_id, operating_region_id, experiment_id, execution_id,
             validation_timestamp, parameter_values, outcome_status, quality_score, notes, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)";

        var paramValuesJson = System.Text.Json.JsonSerializer.Serialize(record.ParameterValues);
        cmd.Parameters.AddWithValue("$1", record.ValidationHistoryId);
        cmd.Parameters.AddWithValue("$2", record.OperatingRegionId);
        cmd.Parameters.AddWithValue("$3", record.ExperimentId);
        cmd.Parameters.AddWithValue("$4", record.ExecutionId);
        cmd.Parameters.AddWithValue("$5", record.ValidationTimestamp);
        cmd.Parameters.Add(new NpgsqlParameter("$6", NpgsqlDbType.Jsonb) { Value = paramValuesJson });
        cmd.Parameters.AddWithValue("$7", record.OutcomeStatus);
        cmd.Parameters.AddWithValue("$8", record.QualityScore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$9", record.Notes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$10", record.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OperatingRegionExtension>> QueryExtensionsAsync(
        string operatingRegionId,
        bool approvedOnly = false,
        CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT extension_id, operating_region_id, triggering_experiment_id,
                   out_of_bounds_parameter_name, out_of_bounds_value,
                   original_min_value, original_max_value, extended_min_value, extended_max_value,
                   extension_approved, approved_by, approval_timestamp, approval_notes, created_at, updated_at
            FROM research_operating_region_extensions
            WHERE operating_region_id = $1";

        cmd.Parameters.AddWithValue("$1", operatingRegionId);

        if (approvedOnly)
            cmd.CommandText += " AND extension_approved = true";

        cmd.CommandText += " ORDER BY created_at DESC";

        var results = new List<OperatingRegionExtension>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(MapExtension(reader));

        return results;
    }

    public async Task SaveExtensionAsync(
        OperatingRegionExtension extension,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO research_operating_region_extensions
            (extension_id, operating_region_id, triggering_experiment_id,
             out_of_bounds_parameter_name, out_of_bounds_value,
             original_min_value, original_max_value, extended_min_value, extended_max_value,
             extension_approved, approved_by, approval_timestamp, approval_notes, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15)";

        cmd.Parameters.AddWithValue("$1", extension.ExtensionId);
        cmd.Parameters.AddWithValue("$2", extension.OperatingRegionId);
        cmd.Parameters.AddWithValue("$3", extension.TriggeringExperimentId);
        cmd.Parameters.AddWithValue("$4", extension.OutOfBoundsParameterName);
        cmd.Parameters.AddWithValue("$5", extension.OutOfBoundsValue);
        cmd.Parameters.AddWithValue("$6", extension.OriginalMinValue);
        cmd.Parameters.AddWithValue("$7", extension.OriginalMaxValue);
        cmd.Parameters.AddWithValue("$8", extension.ExtendedMinValue);
        cmd.Parameters.AddWithValue("$9", extension.ExtendedMaxValue);
        cmd.Parameters.AddWithValue("$10", extension.ExtensionApproved);
        cmd.Parameters.AddWithValue("$11", extension.ApprovedBy ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$12", extension.ApprovalTimestamp ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$13", extension.ApprovalNotes ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$14", extension.CreatedAt);
        cmd.Parameters.AddWithValue("$15", extension.UpdatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ApproveExtensionAsync(
        string extensionId,
        string approvedBy,
        string? notes,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE research_operating_region_extensions
            SET extension_approved = true, approved_by = $2, approval_timestamp = $3,
                approval_notes = $4, updated_at = NOW()
            WHERE extension_id = $1";

        cmd.Parameters.AddWithValue("$1", extensionId);
        cmd.Parameters.AddWithValue("$2", approvedBy);
        cmd.Parameters.AddWithValue("$3", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("$4", notes ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ParameterConstraint?> GetConstraintAsync(
        string constraintId,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT constraint_id, operating_region_id, constraint_type,
                   parameter_name_a, parameter_name_b, constraint_expression, constraint_description, created_at
            FROM research_operating_region_parameter_constraints
            WHERE constraint_id = $1
            LIMIT 1";
        cmd.Parameters.AddWithValue("$1", constraintId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new ParameterConstraint(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7))
            : null;
    }

    public async Task<IReadOnlyList<ParameterConstraint>> ListConstraintsAsync(
        string operatingRegionId,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT constraint_id, operating_region_id, constraint_type,
                   parameter_name_a, parameter_name_b, constraint_expression, constraint_description, created_at
            FROM research_operating_region_parameter_constraints
            WHERE operating_region_id = $1
            ORDER BY constraint_type, parameter_name_a, parameter_name_b";
        cmd.Parameters.AddWithValue("$1", operatingRegionId);

        var results = new List<ParameterConstraint>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new ParameterConstraint(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));

        return results;
    }

    public async Task SaveConstraintAsync(
        ParameterConstraint constraint,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO research_operating_region_parameter_constraints
            (constraint_id, operating_region_id, constraint_type,
             parameter_name_a, parameter_name_b, constraint_expression, constraint_description, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT DO NOTHING";

        cmd.Parameters.AddWithValue("$1", constraint.ConstraintId);
        cmd.Parameters.AddWithValue("$2", constraint.OperatingRegionId);
        cmd.Parameters.AddWithValue("$3", constraint.ConstraintType);
        cmd.Parameters.AddWithValue("$4", constraint.ParameterNameA);
        cmd.Parameters.AddWithValue("$5", constraint.ParameterNameB);
        cmd.Parameters.AddWithValue("$6", constraint.ConstraintExpression);
        cmd.Parameters.AddWithValue("$7", constraint.ConstraintDescription ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$8", constraint.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ValidationHistoryRecord MapValidationHistory(NpgsqlDataReader reader)
    {
        var paramValuesJson = reader.GetString(5);
        var paramValues = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal>>(paramValuesJson)
            ?? new Dictionary<string, decimal>();

        return new ValidationHistoryRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            paramValues,
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9));
    }

    private static OperatingRegionExtension MapExtension(NpgsqlDataReader reader)
    {
        return new OperatingRegionExtension(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset?>(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetFieldValue<DateTimeOffset>(13),
            reader.GetFieldValue<DateTimeOffset>(14));
    }
}
