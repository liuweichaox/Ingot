using Ingot.Contracts.ProcessResearch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     周期、实际配方和检验结果到齐后自动固化实验结果并关闭实验。
///     它不批准实验、不启动设备，也不绕过人工安全边界。
/// </summary>
public sealed class ResearchExperimentAutomationHostedService(
    IProcessResearchStore store,
    IResearchObservationAssembler observationAssembler,
    ResearchExperimentResultMaterializer materializer,
    ResearchProcessWindowMaterializer windowMaterializer,
    ILogger<ResearchExperimentAutomationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await MaterializeReadyExperimentsAsync(stoppingToken).ConfigureAwait(false);
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

                var coveredResultIds = (await store.ListProcessWindowsAsync(project.ProjectId, ct)
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
                        await windowMaterializer.MaterializeCandidateAsync(
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
