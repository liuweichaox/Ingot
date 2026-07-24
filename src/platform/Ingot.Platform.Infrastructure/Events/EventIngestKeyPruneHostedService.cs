using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.Events;

/// <summary>
///     event_ingest_keys 幂等键修剪：production_events 可按保留策略丢弃旧数据块，
///     但键表此前无任何清理机制，会无限增长。本服务按 EventIngest:KeyRetentionDays
///     周期性批量删除超龄键行。默认 0（关闭）。
///     注意：键保留窗口必须大于边缘端最大补传时间跨度，否则超窗重放的旧事件会被当作新事件
///     重复入库——因此强制下限 30 天，且建议不小于 production_events 的 RetentionDays。
/// </summary>
public sealed class EventIngestKeyPruneHostedService(
    IConfiguration configuration,
    IOptions<PlatformEventOptions> options,
    ILogger<EventIngestKeyPruneHostedService> logger) : BackgroundService
{
    private const int MinimumRetentionDays = 30;
    private const int DeleteBatchSize = 50_000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

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

        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await PruneAsync(connectionString, retentionDays, stoppingToken).ConfigureAwait(false);
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

    private static async Task<long> PruneAsync(string connectionString, int retentionDays, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

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
}
