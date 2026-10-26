// 从已登记节点和业务事实中发现系统已知站点，供只读系统概览使用。
using Npgsql;

namespace Ingot.Platform.Infrastructure.Services;

public sealed class SystemSiteCatalog(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<string>> ListKnownSiteIdsAsync(CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT site_id FROM platform_edges WHERE site_id IS NOT NULL
            UNION
            SELECT site_id FROM production_events WHERE site_id IS NOT NULL
            UNION
            SELECT site_id FROM inspection_records WHERE site_id IS NOT NULL
            ORDER BY site_id;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var siteIds = new List<string>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var siteId = reader.GetString(0).Trim();
            if (!string.IsNullOrWhiteSpace(siteId))
                siteIds.Add(siteId);
        }
        return siteIds;
    }
}
