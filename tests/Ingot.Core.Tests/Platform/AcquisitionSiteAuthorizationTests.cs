// 验证采集配置和数据源的站点级资源授权成功、拒绝及跨站边界。

using System.Security.Claims;
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class AcquisitionSiteAuthorizationTests
{
    [Fact]
    public async Task TaskCrud_FiltersListAndHidesCrossSiteReadsAndDeletes()
    {
        var tasks = new MemoryTaskStore(
        [
            Task("owned", "EDGE-A"),
            Task("foreign", "EDGE-B"),
            Task("unmapped", "EDGE-UNKNOWN")
        ]);
        var controller = TaskController(tasks, "SITE-A");

        var visible = ReadData<IngestionTask>(await controller.List(CancellationToken.None));
        var foreignRead = await controller.Get("foreign", 1, CancellationToken.None);
        var foreignDelete = await controller.Delete("foreign", 1, CancellationToken.None);
        var ownedDelete = await controller.Delete("owned", 1, CancellationToken.None);

        Assert.Equal("owned", Assert.Single(visible).TaskId);
        AssertStatus(StatusCodes.Status404NotFound, foreignRead);
        AssertStatus(StatusCodes.Status404NotFound, foreignDelete);
        Assert.IsType<NoContentResult>(ownedDelete);
        Assert.Equal(["owned@1"], tasks.Deleted);
    }

    [Fact]
    public async Task TaskUpsert_RejectsNewVersionOfAnotherSitesLogicalResource()
    {
        var tasks = new MemoryTaskStore(
        [
            Task("shared", "EDGE-B") with
            {
                Status = ConfigurationStatuses.Published
            }
        ]);
        var controller = TaskController(tasks, "SITE-A");

        var result = await controller.Upsert(
            Task("shared", "EDGE-A") with { Version = 2 },
            CancellationToken.None);

        AssertStatus(StatusCodes.Status404NotFound, result);
        Assert.Equal(0, tasks.UpsertCount);
    }

    [Fact]
    public async Task DataSourceCrud_FiltersListAndProtectsReadsWritesAndDeletes()
    {
        var configurations = new MemoryConfigurationStore(
        [
            Source("owned", "EDGE-A"),
            Source("foreign", "EDGE-B"),
            Source("unmapped", "EDGE-UNKNOWN")
        ]);
        var controller = ConfigurationController(configurations, new MemoryTaskStore([]), "SITE-A");

        var visible = ReadData<DataSourceInstance>(
            await controller.ListDataSources(CancellationToken.None));
        var foreignRead = await controller.GetDataSource("foreign", 1, CancellationToken.None);
        var foreignSave = await controller.SaveDataSource(
            Source("new-foreign", "EDGE-B"), CancellationToken.None);
        var foreignDelete = await controller.DeleteDataSource("foreign", 1, CancellationToken.None);
        var ownedSave = await controller.SaveDataSource(
            Source("new-owned", "EDGE-A"), CancellationToken.None);
        var ownedDelete = await controller.DeleteDataSource("owned", 1, CancellationToken.None);

        Assert.Equal("owned", Assert.Single(visible).DataSourceId);
        AssertStatus(StatusCodes.Status404NotFound, foreignRead);
        AssertStatus(StatusCodes.Status404NotFound, foreignSave);
        AssertStatus(StatusCodes.Status404NotFound, foreignDelete);
        Assert.IsType<OkObjectResult>(ownedSave);
        Assert.IsType<NoContentResult>(ownedDelete);
        Assert.Equal(1, configurations.UpsertCount);
        Assert.Equal(["owned@1"], configurations.Deleted);
    }

    [Fact]
    public async Task DataSourceSave_RejectsNewVersionOfAnotherSitesLogicalResource()
    {
        var configurations = new MemoryConfigurationStore(
        [
            Source("shared", "EDGE-B") with
            {
                Status = ConfigurationStatuses.Published
            }
        ]);
        var controller = ConfigurationController(configurations, new MemoryTaskStore([]), "SITE-A");

        var result = await controller.SaveDataSource(
            Source("shared", "EDGE-A") with
            {
                Version = 2,
                Status = ConfigurationStatuses.Published
            },
            CancellationToken.None);

        AssertStatus(StatusCodes.Status404NotFound, result);
        Assert.Equal(0, configurations.PublishCount);
    }

    [Fact]
    public void EdgeSiteLookup_FailsClosedWithoutAnExplicitMappingEvenWhenTokensAreDisabled()
    {
        var validator = new EdgeTokenValidator(Options.Create(new PlatformEventOptions
        {
            RequireToken = false,
            EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-A"] = "SITE-A"
            }
        }));

        Assert.True(validator.TryGetSiteId("edge-a", out var siteId));
        Assert.Equal("SITE-A", siteId);
        Assert.False(validator.TryGetSiteId("EDGE-UNKNOWN", out _));
    }

    private static IngestionTasksController TaskController(MemoryTaskStore tasks, string siteId)
        => new(
            new AcquisitionApplication(tasks, null!),
            new ProcessConfigurationApplication(null!),
            new PlatformUserResolver(new TestHostEnvironment()),
            EdgeValidator(),
            new AcquisitionProbeTaskCoordinator(null!))
        {
            ControllerContext = Context(siteId)
        };

    private static IngestionConfigurationController ConfigurationController(
        MemoryConfigurationStore configurations,
        MemoryTaskStore tasks,
        string siteId)
        => new(
            new AcquisitionApplication(tasks, configurations),
            new IngestionConfigurationWorkflow(configurations, tasks, null!, null!),
            new PlatformUserResolver(new TestHostEnvironment()),
            EdgeValidator())
        {
            ControllerContext = Context(siteId)
        };

    private static EdgeTokenValidator EdgeValidator()
        => new(Options.Create(new PlatformEventOptions
        {
            EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-A"] = "SITE-A",
                ["EDGE-B"] = "SITE-B"
            }
        }));

    private static ControllerContext Context(string siteId)
        => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "engineer"),
                    new Claim(ClaimTypes.Role, PlatformRoles.ProcessEngineer),
                    new Claim(PlatformClaimTypes.SiteId, siteId)
                ], "test"))
            }
        };

    private static IngestionTask Task(string taskId, string edgeId) => new()
    {
        TaskId = taskId,
        Name = taskId,
        EdgeId = edgeId,
        Protocol = AcquisitionProtocols.HttpPolling,
        DataModelId = "model",
        Source = $"connector/http/{taskId}",
        SubjectId = taskId,
        TimestampMode = "edge-received",
        HttpPolling = new HttpPollingConnection
        {
            BaseUrl = "http://192.168.10.10",
            SnapshotPath = "/snapshot"
        },
        ValueMappings =
        [
            new AcquisitionValueMapping
            {
                DataItemCode = "press.temperature",
                SourcePath = "temperature"
            }
        ]
    };

    private static DataSourceInstance Source(string sourceId, string edgeId) => new()
    {
        DataSourceId = sourceId,
        Name = sourceId,
        EdgeId = edgeId,
        Protocol = AcquisitionProtocols.HttpPolling,
        SourceKey = $"connector/http/{sourceId}",
        SubjectId = sourceId,
        HttpPolling = new HttpPollingConnection
        {
            BaseUrl = "http://192.168.10.10",
            SnapshotPath = "/snapshot"
        }
    };

    private static T[] ReadData<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        return JsonSerializer.Deserialize<T[]>(
                   document.RootElement.GetProperty("data"),
                   new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    private static void AssertStatus(int expected, IActionResult result)
        => Assert.Equal(expected, Assert.IsType<ObjectResult>(result).StatusCode);

    private sealed class MemoryTaskStore(IEnumerable<IngestionTask> seed) : IIngestionTaskStore
    {
        private readonly List<IngestionTask> tasks = [.. seed];

        public int UpsertCount { get; private set; }
        public List<string> Deleted { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;

        public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTask>>([.. tasks]);

        public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(
            string edgeId,
            CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTask>>(
                tasks.Where(task => task.EdgeId == edgeId && task.Status == ConfigurationStatuses.Published).ToArray());

        public Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IngestionTask?>(tasks.SingleOrDefault(task =>
                string.Equals(task.TaskId, taskId, StringComparison.OrdinalIgnoreCase) && task.Version == version));

        public Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
        {
            UpsertCount++;
            tasks.RemoveAll(task => task.TaskId == value.TaskId && task.Version == value.Version);
            tasks.Add(value);
            return System.Threading.Tasks.Task.FromResult(value);
        }

        public Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default)
            => UpsertAsync(published, ct);

        public Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
        {
            var removed = tasks.RemoveAll(task => task.TaskId == taskId && task.Version == version) > 0;
            if (removed) Deleted.Add($"{taskId}@{version}");
            return System.Threading.Tasks.Task.FromResult(removed);
        }
    }

    private sealed class MemoryConfigurationStore(IEnumerable<DataSourceInstance> seed)
        : IIngestionConfigurationStore
    {
        private readonly List<DataSourceInstance> sources = [.. seed];

        public int UpsertCount { get; private set; }
        public int PublishCount { get; private set; }
        public List<string> Deleted { get; } = [];

        public Task<IReadOnlyList<DataSourceInstance>> ListDataSourcesAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DataSourceInstance>>([.. sources]);

        public Task<DataSourceInstance?> GetDataSourceAsync(
            string dataSourceId,
            int version,
            CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<DataSourceInstance?>(sources.SingleOrDefault(source =>
                string.Equals(source.DataSourceId, dataSourceId, StringComparison.OrdinalIgnoreCase) &&
                source.Version == version));

        public Task<DataSourceInstance> UpsertDataSourceAsync(
            DataSourceInstance value,
            CancellationToken ct = default)
        {
            UpsertCount++;
            Save(value);
            return System.Threading.Tasks.Task.FromResult(value);
        }

        public Task<DataSourceInstance> PublishDataSourceExclusiveAsync(
            DataSourceInstance value,
            CancellationToken ct = default)
        {
            PublishCount++;
            Save(value);
            return System.Threading.Tasks.Task.FromResult(value);
        }

        public Task<IReadOnlyList<DataSourceInstance>> SaveDataSourcesAsync(
            IReadOnlyList<DataSourceInstance> values,
            CancellationToken ct = default)
        {
            foreach (var value in values) Save(value);
            return System.Threading.Tasks.Task.FromResult(values);
        }

        public Task<bool> DeleteDataSourceAsync(
            string dataSourceId,
            int version,
            CancellationToken ct = default)
        {
            var removed = sources.RemoveAll(source =>
                source.DataSourceId == dataSourceId && source.Version == version) > 0;
            if (removed) Deleted.Add($"{dataSourceId}@{version}");
            return System.Threading.Tasks.Task.FromResult(removed);
        }

        private void Save(DataSourceInstance value)
        {
            sources.RemoveAll(source =>
                source.DataSourceId == value.DataSourceId && source.Version == value.Version);
            sources.Add(value);
        }

        public Task<IReadOnlyList<IngestionTaskTemplate>> ListTemplatesAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTaskTemplate>>([]);
        public Task<IngestionTaskTemplate?> GetTemplateAsync(string templateId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IngestionTaskTemplate?>(null);
        public Task<IngestionTaskTemplate> UpsertTemplateAsync(IngestionTaskTemplate value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTaskTemplate> PublishTemplateExclusiveAsync(IngestionTaskTemplate value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteTemplateAsync(string templateId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTaskBinding>> ListBindingsAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTaskBinding>>([]);
        public Task<IngestionTaskBinding?> GetBindingAsync(string taskId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IngestionTaskBinding?>(null);
        public Task<IReadOnlyList<IngestionTask>> SaveMaterializedTasksAsync(
            IReadOnlyList<(IngestionTaskBinding Binding, IngestionTask Task)> values,
            CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ReusableIngestionConfiguration> SaveExtractedAsync(
            ReusableIngestionConfiguration value,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
