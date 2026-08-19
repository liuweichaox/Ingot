using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ingot.Agent.Providers;

/// <summary>
///     One-way, idempotent cutover for deployments that already have Data/chat.db.
///     The source file is retained so operators can include it in rollback backups.
/// </summary>
public sealed class LegacySqliteAgentRunImporterHostedService(
    SqliteAgentStore legacy,
    IAgentRunStore primary,
    IConfiguration configuration,
    ILogger<LegacySqliteAgentRunImporterHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (ReferenceEquals(legacy, primary) ||
            !configuration.GetValue("Chat:ImportLegacySqlite", true) ||
            !legacy.DatabaseExistedAtConstruction ||
            primary is not IAgentRunImportStore importer)
            return;

        var imported = 0;
        var skipped = 0;
        foreach (var run in await legacy.ExportAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var events = await legacy.ExportEventsAsync(run.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (await importer.ImportAsync(run, events, cancellationToken).ConfigureAwait(false))
                imported++;
            else
                skipped++;
        }
        logger.LogInformation(
            "Legacy SQLite Agent run import complete. Imported={Imported}, Skipped={Skipped}.",
            imported,
            skipped);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
