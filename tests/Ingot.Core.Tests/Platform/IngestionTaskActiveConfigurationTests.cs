// 验证平台组件 IngestionTaskActiveConfiguration 的成功、拒绝和安全边界。

using System.Security.Claims;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Api.Errors;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class IngestionTaskActiveConfigurationTests
{
    [Fact]
    public async Task ActiveConfigurationFailsClosedWhenPublishedTaskModelIsUnavailable()
    {
        var controller = new IngestionTasksController(
            new AcquisitionApplication(new PublishedTaskStore(), null!),
            new ProcessConfigurationApplication(new MissingModelStore()),
            new PlatformUserResolver(new TestHostEnvironment()),
            new EdgeTokenValidator(Options.Create(new PlatformEventOptions
            {
                RequireToken = false,
                EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EDGE-001"] = "SITE-001"
                }
            })),
            new AcquisitionProbeTaskCoordinator(new UnusedProbeTaskStore()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Active("EDGE-001", CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("避免误停采", Assert.IsType<ApiProblemDetails>(conflict.Value).Detail);
    }

    [Fact]
    public async Task ActiveConfigurationRejectsAnUnmappedEdgeEvenWhenTokensAreDisabled()
    {
        var controller = new IngestionTasksController(
            new AcquisitionApplication(new PublishedTaskStore(), null!),
            new ProcessConfigurationApplication(new MissingModelStore()),
            new PlatformUserResolver(new TestHostEnvironment()),
            new EdgeTokenValidator(Options.Create(new PlatformEventOptions { RequireToken = false })),
            new AcquisitionProbeTaskCoordinator(new UnusedProbeTaskStore()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Active("EDGE-001", CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task ProbeRejectsProcessEngineerWhoseSiteDoesNotOwnTheEdge()
    {
        var probeStore = new ImmediateProbeTaskStore();
        var controller = CreateProbeController("SITE-A", probeStore);

        var result = await controller.Probe(
            new IngestionTaskProbeRequest { Task = ProbeTask("EDGE-B") },
            CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, denied.StatusCode);
        Assert.Equal(0, probeStore.EnqueuedCount);
    }

    [Fact]
    public async Task ProbeAllowsProcessEngineerForTheEdgesBoundSite()
    {
        var probeStore = new ImmediateProbeTaskStore();
        var controller = CreateProbeController("SITE-B", probeStore);

        var result = await controller.Probe(
            new IngestionTaskProbeRequest { Task = ProbeTask("EDGE-B") },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, probeStore.EnqueuedCount);
    }

    [Fact]
    public async Task PublishedUpsertCannotUseProbePathForAnotherSitesEdge()
    {
        var probeStore = new ImmediateProbeTaskStore();
        var controller = CreateProbeController("SITE-A", probeStore);

        var result = await controller.Upsert(
            ProbeTask("EDGE-B") with { Status = ConfigurationStatuses.Published },
            CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, denied.StatusCode);
        Assert.Equal(0, probeStore.EnqueuedCount);
    }

    [Fact]
    public async Task UpsertCannotMasqueradeAsOwnedEdgeToRetireAnotherSitesExistingTask()
    {
        var probeStore = new ImmediateProbeTaskStore();
        var taskStore = new PublishedTaskStore();
        var controller = CreateProbeController("SITE-A", probeStore, taskStore);

        var result = await controller.Upsert(
            ProbeTask("EDGE-A") with
            {
                TaskId = "press-01",
                Status = ConfigurationStatuses.Retired
            },
            CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, denied.StatusCode);
        Assert.Equal(0, taskStore.UpsertCount);
        Assert.Equal(0, probeStore.EnqueuedCount);
    }

    private static IngestionTasksController CreateProbeController(
        string identitySite,
        ImmediateProbeTaskStore probeStore,
        PublishedTaskStore? taskStore = null)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "engineer"),
                new Claim(ClaimTypes.Role, PlatformRoles.ProcessEngineer),
                new Claim(PlatformClaimTypes.SiteId, identitySite)
            ], "test"))
        };
        return new IngestionTasksController(
            new AcquisitionApplication(taskStore ?? new PublishedTaskStore(), null!),
            new ProcessConfigurationApplication(new MissingModelStore(ProbeModel())),
            new PlatformUserResolver(new TestHostEnvironment(Environments.Production)),
            new EdgeTokenValidator(Options.Create(new PlatformEventOptions
            {
                EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EDGE-A"] = "SITE-A",
                    ["EDGE-B"] = "SITE-B",
                    ["EDGE-001"] = "SITE-B"
                }
            })),
            new AcquisitionProbeTaskCoordinator(probeStore))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static IngestionTask ProbeTask(string edgeId) => new()
    {
        TaskId = "probe-task",
        Name = "Probe task",
        EdgeId = edgeId,
        Protocol = AcquisitionProtocols.HttpPolling,
        DataModelId = "probe-model",
        Source = "connector/http/probe",
        SubjectId = "PRESS-01",
        TimestampMode = "edge-received",
        HttpPolling = new HttpPollingConnection
        {
            BaseUrl = "http://192.168.10.10",
            SnapshotPath = "/snapshot"
        },
        Execution = new AcquisitionExecutionOptions { TimeoutMs = 1000 },
        ValueMappings =
        [
            new AcquisitionValueMapping
            {
                DataItemCode = "press.temperature",
                SourcePath = "temperature"
            }
        ]
    };

    private static ProcessDataModel ProbeModel() => new()
    {
        ModelId = "probe-model",
        Name = "Probe model",
        Acquisition = new AcquisitionModel
        {
            DataItems =
            [
                new ProcessDataItemDefinition
                {
                    Code = "press.temperature",
                    DisplayName = "Temperature"
                }
            ]
        }
    };

    private sealed class PublishedTaskStore : IIngestionTaskStore
    {
        private static readonly IngestionTask Task = new()
        {
            TaskId = "press-01",
            Name = "Press 01",
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            Protocol = AcquisitionProtocols.HttpPolling,
            DataModelId = "press-model",
            DataModelVersion = 1,
            Source = "connector/http/press-01",
            SubjectId = "press-01",
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "temperature.actual",
                    SourcePath = "temperature"
                }
            ]
        };

        public int UpsertCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTask>>([Task]);
        public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTask>>([Task]);
        public Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IngestionTask?>(
                string.Equals(taskId, Task.TaskId, StringComparison.Ordinal) && version == Task.Version
                    ? Task
                    : null);
        public Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
        {
            UpsertCount++;
            return System.Threading.Tasks.Task.FromResult(value);
        }
        public Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(published);
        public Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(false);
    }

    private sealed class MissingModelStore(ProcessDataModel? model = null) : IProcessConfigurationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(value);
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ProcessDataModel>>([]);
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(
                model is not null && model.ModelId == modelId && model.Version == version ? model : null);
        public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(false);
        public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(value);
        public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ProcessSpecification>>([]);
        public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<ProcessSpecification?>(null);
        public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(false);
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(value);
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ProcessAnalysisPlan>>([]);
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<ProcessAnalysisPlan?>(null);
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(false);
        public Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ScenarioPackage>>([]);
        public Task<ScenarioPackage?> GetScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<ScenarioPackage?>(null);
        public Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(false);
    }

    private sealed class TestHostEnvironment(string environmentName = "Development") : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Ingot.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class UnusedProbeTaskStore : IAcquisitionProbeTaskStore
    {
        public Task EnqueueAsync(AcquisitionProbeTask task, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AcquisitionProbeTask?> ClaimNextAsync(string edgeId, CancellationToken ct = default) => Task.FromResult<AcquisitionProbeTask?>(null);
        public Task<bool> CompleteAsync(AcquisitionProbeTaskCompletion completion, CancellationToken ct = default) => Task.FromResult(false);
        public Task<AcquisitionProbeResult?> GetResultAsync(string taskId, CancellationToken ct = default) => Task.FromResult<AcquisitionProbeResult?>(null);
        public Task DeleteAsync(string taskId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ImmediateProbeTaskStore : IAcquisitionProbeTaskStore
    {
        private string? taskId;

        public int EnqueuedCount { get; private set; }

        public Task EnqueueAsync(AcquisitionProbeTask task, CancellationToken ct = default)
        {
            taskId = task.TaskId;
            EnqueuedCount++;
            return Task.CompletedTask;
        }

        public Task<AcquisitionProbeTask?> ClaimNextAsync(string edgeId, CancellationToken ct = default)
            => Task.FromResult<AcquisitionProbeTask?>(null);

        public Task<bool> CompleteAsync(AcquisitionProbeTaskCompletion completion, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<AcquisitionProbeResult?> GetResultAsync(string requestedTaskId, CancellationToken ct = default)
            => Task.FromResult<AcquisitionProbeResult?>(requestedTaskId == taskId
                ? new AcquisitionProbeResult
                {
                    Success = true,
                    MappingsValidated = true,
                    Protocol = AcquisitionProtocols.HttpPolling,
                    Message = "ok",
                    TestedAt = DateTimeOffset.UtcNow
                }
                : null);

        public Task DeleteAsync(string requestedTaskId, CancellationToken ct = default)
        {
            if (requestedTaskId == taskId)
                taskId = null;
            return Task.CompletedTask;
        }
    }
}
