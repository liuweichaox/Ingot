using Ingot.Platform.Application.ProcessResearch;
using Ingot.Contracts.ProcessResearch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     过程执行、实际工艺规范和检验结果到齐后自动固化实验结果并关闭实验。
///     它不批准实验、不启动设备，也不绕过人工安全边界。
/// </summary>
public sealed class ResearchExperimentAutomationHostedService(
    NpgsqlDataSource dataSource,
    IProcessResearchStore store,
    IResearchObservationAssembler observationAssembler,
    ResearchExperimentResultMaterializer materializer,
    ResearchOperatingRegionMaterializer operatingRegionMaterializer,
    ILogger<ResearchExperimentAutomationHostedService> logger) : BackgroundService
{
    private const long AdvisoryLockKey = 0x496E676F74524553; // IngotRES

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await MaterializeAsLeaderAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "自动回收研发实验结果失败；下一轮将重试");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task MaterializeAsLeaderAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using (var acquire = new NpgsqlCommand(
                         $"SELECT pg_try_advisory_lock({AdvisoryLockKey});", connection))
        {
            if (await acquire.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                return;
        }
        try
        {
            await MaterializeReadyExperimentsAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                $"SELECT pg_advisory_unlock({AdvisoryLockKey});", connection);
            await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task MaterializeReadyExperimentsAsync(CancellationToken ct)
    {
        const int pageSize = 100;
        for (var offset = 0;; offset += pageSize)
        {
            var projects = await store.ListProjectsAsync(
                "system-research-automation",
                true,
                pageSize,
                offset,
                ct).ConfigureAwait(false);
            foreach (var project in projects.Where(static value =>
                         value.Status is ResearchProjectStatuses.Active
                             or ResearchProjectStatuses.Validating))
            {
                var experiments = await store.ListExperimentsAsync(project.ProjectId, ct)
                    .ConfigureAwait(false);
                var results = await store.ListExperimentResultsAsync(project.ProjectId, ct)
                    .ConfigureAwait(false);
                if (experiments.Any(static value =>
                        value.Status == ResearchExperimentStatuses.Running))
                {
                    var assembly = await observationAssembler.AssembleAsync(project, experiments, ct)
                        .ConfigureAwait(false);
                    var created = await materializer.MaterializeCompletedAsync(
                        project,
                        experiments,
                        results,
                        assembly,
                        "system-research-automation",
                        ct).ConfigureAwait(false);
                    if (created.Count > 0)
                    {
                        logger.LogInformation(
                            "已自动固化研发项目 {ProjectId} 的 {ResultCount} 份实验结果",
                            project.ProjectId,
                            created.Count);
                        experiments = await store.ListExperimentsAsync(project.ProjectId, ct)
                            .ConfigureAwait(false);
                        results = await store.ListExperimentResultsAsync(project.ProjectId, ct)
                            .ConfigureAwait(false);
                    }
                }

                var coveredResultIds = (await store.ListOperatingRegionsAsync(project.ProjectId, ct)
                        .ConfigureAwait(false))
                    .SelectMany(static value => value.SupportingResultIds)
                    .ToHashSet();
                foreach (var experiment in experiments.Where(static value =>
                             value.Status == ResearchExperimentStatuses.Completed &&
                             value.Optimization is not null))
                {
                    foreach (var result in results.Where(value =>
                                 value.ExperimentId == experiment.ExperimentId &&
                                 !coveredResultIds.Contains(value.ResultId)))
                    {
                        await operatingRegionMaterializer.MaterializeCandidateAsync(
                            project,
                            experiment,
                            result,
                            "system-research-automation",
                            ct).ConfigureAwait(false);
                    }
                }
            }
            if (projects.Count < pageSize)
                break;
        }
    }
}
