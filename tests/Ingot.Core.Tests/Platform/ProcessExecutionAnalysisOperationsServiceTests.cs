// 验证平台组件 ProcessExecutionAnalysisOperationsService 的成功、拒绝和安全边界。

using Ingot.Contracts.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessExecutionAnalysisOperationsServiceTests
{
    [Fact]
    public async Task EnqueueBackfillAsync_ShouldNormalizeFiltersInsideApplicationUseCase()
    {
        var store = new OperationsStore();
        var service = new ProcessExecutionAnalysisOperationsService(
            store,
            new BoundaryStore(),
            new ExecutionService(false));

        var job = await service.EnqueueBackfillAsync(
            new ProcessExecutionAnalysisBackfillRequest
            {
                SiteId = " SITE-A ",
                ProductCode = " product-a ",
                EquipmentId = "  ",
                PageSize = 100
            },
            "engineer-1");

        Assert.Same(job, store.AddedJob);
        Assert.Equal("SITE-A", job.Request.SiteId);
        Assert.Equal("product-a", job.Request.ProductCode);
        Assert.Null(job.Request.EquipmentId);
        Assert.Equal("engineer-1", job.CreatedBy);
    }

    [Fact]
    public async Task ReplayAnalysisAsync_ShouldAuthorizeExecutionWithinSiteBeforeRequeue()
    {
        var store = new OperationsStore { ReplayResult = true };
        var executions = new ExecutionService(true);
        var service = new ProcessExecutionAnalysisOperationsService(
            store,
            new BoundaryStore(),
            executions);

        var result = await service.ReplayAnalysisAsync("site-a", "execution-1");

        Assert.Equal(ProcessExecutionReplayResult.Accepted, result);
        Assert.Equal("site-a", executions.SiteId);
        Assert.Equal("execution-1", executions.ExecutionId);
        Assert.Equal("execution-1", store.ReplayedExecutionId);
    }

    [Fact]
    public async Task ReplayAnalysisAsync_DeniesExecutionIdObservedAtMultipleSites()
    {
        var store = new OperationsStore
        {
            ReplayResult = true,
            ExecutionSites = ["site-a", "site-b"]
        };
        var service = new ProcessExecutionAnalysisOperationsService(
            store,
            new BoundaryStore(),
            new ExecutionService(true));

        var result = await service.ReplayAnalysisAsync("site-a", "execution-1");

        Assert.Equal(ProcessExecutionReplayResult.ExecutionNotFound, result);
        Assert.Null(store.ReplayedExecutionId);
    }

    private sealed class OperationsStore : IProcessExecutionAnalysisOperationsStore
    {
        public ProcessExecutionAnalysisBackfillJob? AddedJob { get; private set; }
        public bool ReplayResult { get; init; }
        public string? ReplayedExecutionId { get; private set; }
        public IReadOnlyList<string> ExecutionSites { get; init; } = ["site-a"];

        public Task<ProcessExecutionAnalysisBackfillJob> AddBackfillJobAsync(
            ProcessExecutionAnalysisBackfillJob job,
            CancellationToken ct = default)
        {
            AddedJob = job;
            return Task.FromResult(job);
        }

        public Task<bool> ReplayFailedRecomputeAsync(
            string executionId,
            CancellationToken ct = default)
        {
            ReplayedExecutionId = executionId;
            return Task.FromResult(ReplayResult);
        }

        public Task<IReadOnlyList<string>> ResolveExecutionSitesAsync(
            string executionId,
            CancellationToken ct = default) => Task.FromResult(ExecutionSites);

        public Task<ProcessExecutionAnalysisBackfillJob?> GetBackfillJobAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<ProcessExecutionAnalysisBackfillJob?>(null);

        public Task<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>> ListBackfillJobsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>>([]);

        public Task<IReadOnlyList<ProcessExecutionFeatureAggregate>> QueryFeatureAggregatesAsync(
            string siteId,
            string? signalCode, string? phaseCode, string? featureCode,
            DateTimeOffset? from, DateTimeOffset? to, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProcessExecutionFeatureAggregate>>([]);
    }

    private sealed class BoundaryStore : IExecutionBoundaryStore
    {
        public Task<ExecutionBoundary?> GetBoundaryAsync(
            string siteId,
            string sourceExecutionId,
            CancellationToken ct) => Task.FromResult<ExecutionBoundary?>(null);

        public Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(
            string siteId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int limit = 100,
            int offset = 0,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionBoundary>>([]);

        public Task<bool> ReplayFailedProjectionAsync(string siteId, string sourceExecutionId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class ExecutionService(bool exists) : IProcessExecutionService
    {
        public string? SiteId { get; private set; }
        public string? ExecutionId { get; private set; }

        public Task<ProcessExecutionQueryResult> QueryAsync(
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? productFamilyCode,
            string? productCode,
            string? processSpecificationId,
            string? equipmentId,
            string? outputItemId,
            string? executionId,
            string? status,
            int limit,
            int offset = 0,
            string? search = null,
            CancellationToken ct = default,
            string? edgeId = null,
            string? externalBatchRef = null,
            string? siteId = null)
        {
            SiteId = siteId;
            ExecutionId = executionId;
            return Task.FromResult(new ProcessExecutionQueryResult
            {
                Data = exists
                    ?
                    [
                        new ProcessExecutionSummary
                        {
                            ExecutionId = executionId!,
                            SiteId = siteId!,
                            EquipmentId = "equipment-1",
                            Status = "completed",
                            StartedAt = DateTimeOffset.UtcNow
                        }
                    ]
                    : []
            });
        }
    }
}
