// 验证平台组件 IngestionTaskActiveConfiguration 的成功、拒绝和安全边界。

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
            new EdgeTokenValidator(Options.Create(new PlatformEventOptions { RequireToken = false })),
            new AcquisitionProbeTaskCoordinator(new UnusedProbeTaskStore()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Active("EDGE-001", CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("避免误停采", Assert.IsType<ApiProblemDetails>(conflict.Value).Detail);
    }

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

        public Task InitializeAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTask>>([Task]);
        public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<IngestionTask>>([Task]);
        public Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IngestionTask?>(Task);
        public Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(value);
        public Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(published);
        public Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(false);
    }

    private sealed class MissingModelStore : IProcessConfigurationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => System.Threading.Tasks.Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(value);
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ProcessDataModel>>([]);
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult<ProcessDataModel?>(null);
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
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
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
}
