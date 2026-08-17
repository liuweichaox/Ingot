using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.TimeSeries;

/// <summary>
/// Drops value chunks before their matching frame chunks in one transaction so readers
/// never observe one-sided retention. Disabled when EventIngest:RetentionDays is zero.
/// </summary>
public sealed class TimeSeriesRetentionHostedService(
    IConfiguration configuration,
    IOptions<PlatformEventOptions> options,
    ILogger<TimeSeriesRetentionHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retentionDays = options.Value.RetentionDays;
        if (retentionDays <= 0)
            return;
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(connectionString, retentionDays, stoppingToken).ConfigureAwait(false);
                logger.LogInformation("过程采样帧和值已完成成对保留清理（保留 {Days} 天）", retentionDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "过程采样成对保留清理失败，将在下个周期重试");
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

    internal static async Task PruneAsync(
        string connectionString,
        int retentionDays,
        CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT drop_chunks(
              'process_sample_values',
              older_than => now() - make_interval(days => @days));
            SELECT drop_chunks(
              'process_sample_frames',
              older_than => now() - make_interval(days => @days));
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("days", retentionDays);
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
