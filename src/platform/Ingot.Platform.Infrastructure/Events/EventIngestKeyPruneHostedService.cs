using Ingot.Platform.Application.Events;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Events;

public sealed class EventIngestKeyPruneHostedService(
    NpgsqlDataSource dataSource,
    IOptions<PlatformEventOptions> options,
    ILogger<EventIngestKeyPruneHostedService> logger) : BackgroundService
{
    private const int MinimumRetentionDays = 30;
    private const int DeleteBatchSize = 50_000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const long AdvisoryLockKey = 0x496E676F744B4559;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value.KeyRetentionDays;
        if (configured <= 0)
            return;

        var retentionDays = Math.Max(configured, MinimumRetentionDays);
        if (retentionDays != configured)
            logger.LogWarning(
                "EventIngest:KeyRetentionDays={Configured} 低于安全下限，已提升为 {Effective} 天。",
                configured,
                retentionDays);
        if (options.Value.RetentionDays > 0 && retentionDays < options.Value.RetentionDays)
            logger.LogWarning(
                "键保留（{KeyDays} 天）短于事件保留（{EventDays} 天）：超窗补传的旧事件将无法去重，建议对齐两者。",
                retentionDays,
                options.Value.RetentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await PruneAsync(dataSource, retentionDays, stoppingToken).ConfigureAwait(false);
                if (deleted > 0)
                    logger.LogInformation("event_ingest_keys 修剪完成：删除 {Deleted} 行（保留 {Days} 天）。", deleted, retentionDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "event_ingest_keys 修剪失败，将在下个周期重试。");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task<long> PruneAsync(NpgsqlDataSource dataSource, int retentionDays, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (var acquire = new NpgsqlCommand(
                         $"SELECT pg_try_advisory_lock({AdvisoryLockKey});", connection))
        {
            if (await acquire.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                return 0;
        }

        try
        {
            long total = 0;
            while (!ct.IsCancellationRequested)
            {
                await using var command = new NpgsqlCommand(
                """
                DELETE FROM event_ingest_keys
                WHERE ctid IN (
                  SELECT ctid FROM event_ingest_keys
                  WHERE occurred_at < now() - make_interval(days => @days)
                  LIMIT @batch
                );
                """,
                connection);
                command.Parameters.AddWithValue("days", retentionDays);
                command.Parameters.AddWithValue("batch", DeleteBatchSize);
                command.CommandTimeout = 300;
                var deleted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                total += deleted;
                if (deleted < DeleteBatchSize)
                    break;
            }
            return total;
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                $"SELECT pg_advisory_unlock({AdvisoryLockKey});", connection);
            await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
