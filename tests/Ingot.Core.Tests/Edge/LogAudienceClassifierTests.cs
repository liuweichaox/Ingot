// 验证边缘组件 LogAudienceClassifier 的协议、状态和失败边界。

using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Infrastructure.Logs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class LogAudienceClassifierTests
{
    [Theory]
    [InlineData("Information", "Microsoft.Hosting.Lifetime")]
    [InlineData("Information", "Ingot.Edge.ConnectorHost.Program")]
    public void Classify_ShouldHideFrameworkAndStartupInformationByDefault(string level, string source)
    {
        var result = LogAudienceClassifier.Classify(level, source);

        Assert.Equal(LogAudiences.System, result.Audience);
    }

    [Theory]
    [InlineData("Information", "Ingot.Edge.Infrastructure.Acquisition.AcquisitionWorker", "设备采集")]
    [InlineData("Information", "Ingot.Edge.Infrastructure.Outbox.EventShipmentWorker", "数据上行")]
    [InlineData("Information", "Ingot.Edge.Infrastructure.Configuration.DeploymentWorker", "配置应用")]
    [InlineData("Error", "Microsoft.AspNetCore.Server.Kestrel", "节点服务")]
    public void Classify_ShouldExposeActionableEvents(string level, string source, string category)
    {
        var result = LogAudienceClassifier.Classify(level, source);

        Assert.Equal(LogAudiences.Operator, result.Audience);
        Assert.Equal(category, result.Category);
    }

    [Fact]
    public async Task SqliteLogViewService_ShouldFilterBeforePagination()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ingot-log-view-{Guid.NewGuid():N}.db");
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE Logs(
                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                      TimeStamp TEXT NOT NULL,
                      Level TEXT NOT NULL,
                      Properties TEXT,
                      Message TEXT NOT NULL,
                      Exception TEXT);
                    INSERT INTO Logs(TimeStamp, Level, Properties, Message) VALUES
                      ('2026-08-20T08:00:03Z', 'Information', '{"SourceContext":"Microsoft.Hosting.Lifetime"}', 'started'),
                      ('2026-08-20T08:00:02Z', 'Information', '{"SourceContext":"Ingot.Edge.Infrastructure.Acquisition.AcquisitionWorker"}', 'sample rejected'),
                      ('2026-08-20T08:00:01Z', 'Error', '{"SourceContext":"Microsoft.AspNetCore.Server.Kestrel"}', 'listener failed');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            using var service = new SqliteLogViewService(Options.Create(new LogOptions { DatabasePath = databasePath }));
            var (operatorEntries, operatorTotal) = await service.GetLogsAsync(
                audience: LogAudiences.Operator,
                skip: 0,
                take: 1);
            var (systemEntries, systemTotal) = await service.GetLogsAsync(
                audience: LogAudiences.System,
                skip: 0,
                take: 10);

            Assert.Equal(2, operatorTotal);
            Assert.Single(operatorEntries);
            Assert.Equal("设备采集", operatorEntries[0].Category);
            Assert.Equal(1, systemTotal);
            Assert.Single(systemEntries);
            Assert.Equal("started", systemEntries[0].Message);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
