// Verify site filtering is applied before the review query limit in PostgreSQL.

using Ingot.Platform.Infrastructure.Inspections;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresInspectionReviewStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task QueryAsync_FiltersBySiteBeforeLimit_AndNullSiteKeepsGlobalQuery()
    {
        await postgres.EnsureSchemaAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var siteA = $"SITE-A-{suffix}";
        var siteB = $"SITE-B-{suffix}";
        var executionId = $"RUN-{suffix}";
        var recordA = Guid.CreateVersion7();
        var recordB = Guid.CreateVersion7();
        var reviewA = Guid.CreateVersion7();
        var reviewB = Guid.CreateVersion7();
        var reviewedAt = DateTimeOffset.UtcNow;

        await using (var recordsCommand = postgres.DataSource.CreateCommand(
            """
            INSERT INTO inspection_records(
              record_id, site_id, execution_id, definition_code, definition_version,
              measured_at, recorded_at, outcome, submitted_by, submitter_verified, payload_hash)
            VALUES
              (@record_a, @site_a, @execution_id, 'integration.check', 1, @measured_at, @recorded_at, 'PASS', 'tester', true, 'record-a'),
              (@record_b, @site_b, @execution_id, 'integration.check', 1, @measured_at, @recorded_at, 'PASS', 'tester', true, 'record-b');
            """))
        {
            recordsCommand.Parameters.AddWithValue("record_a", recordA);
            recordsCommand.Parameters.AddWithValue("record_b", recordB);
            recordsCommand.Parameters.AddWithValue("site_a", siteA);
            recordsCommand.Parameters.AddWithValue("site_b", siteB);
            recordsCommand.Parameters.AddWithValue("execution_id", executionId);
            recordsCommand.Parameters.AddWithValue("measured_at", reviewedAt.AddHours(-2).UtcDateTime);
            recordsCommand.Parameters.AddWithValue("recorded_at", reviewedAt.AddHours(-2).UtcDateTime);
            await recordsCommand.ExecuteNonQueryAsync();
        }
        await using (var reviewsCommand = postgres.DataSource.CreateCommand(
            """
            INSERT INTO inspection_reviews(
              review_id, inspection_record_id, execution_id, decision, reviewed_at, reviewed_by, payload_hash)
            VALUES
              (@review_a, @record_a, @execution_id, 'CONFIRMED', @older_reviewed_at, 'tester', 'review-a'),
              (@review_b, @record_b, @execution_id, 'CONFIRMED', @newer_reviewed_at, 'tester', 'review-b');
            """))
        {
            reviewsCommand.Parameters.AddWithValue("review_a", reviewA);
            reviewsCommand.Parameters.AddWithValue("review_b", reviewB);
            reviewsCommand.Parameters.AddWithValue("record_a", recordA);
            reviewsCommand.Parameters.AddWithValue("record_b", recordB);
            reviewsCommand.Parameters.AddWithValue("execution_id", executionId);
            reviewsCommand.Parameters.AddWithValue("older_reviewed_at", reviewedAt.AddMinutes(-1).UtcDateTime);
            reviewsCommand.Parameters.AddWithValue("newer_reviewed_at", reviewedAt.UtcDateTime);
            await reviewsCommand.ExecuteNonQueryAsync();
        }

        var store = new PostgresInspectionReviewStore(postgres.DataSource);
        var scoped = await store.QueryAsync(null, executionId, 1, siteId: siteA);
        var global = await store.QueryAsync(null, executionId, 1);

        Assert.Equal(recordA, Assert.Single(scoped).InspectionRecordId);
        Assert.Equal(recordB, Assert.Single(global).InspectionRecordId);
    }
}
