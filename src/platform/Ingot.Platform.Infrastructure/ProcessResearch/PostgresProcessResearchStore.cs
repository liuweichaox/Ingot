// 在 PostgreSQL 中持久化下一配方建议及其只追加的决定、运行关联和结果证据。
using System.Text.Json;
using Ingot.Platform.Application.ProcessResearch;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class PostgresProcessResearchStore : IProcessResearchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;

    public PostgresProcessResearchStore(NpgsqlDataSource dataSource)
        => _dataSource = dataSource;
}
