// 验证研发项目直接围绕真实运行证据进入和完成生命周期。
using Ingot.Contracts.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessResearchWorkflowProjectLifecycleTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task Project_CanStartWithoutASeparateValidationWorkflow()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "production-evidence-only" }, "engineer-a");

        var started = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a",
            expectedRevision: project.Revision);

        Assert.Equal(ResearchProjectStatuses.Active, started.Status);
    }

    [Fact]
    public async Task StandaloneProject_CanStartWithoutAProductionScope()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with
            {
                Code = "standalone-research",
                SiteCode = null,
                Context = new Dictionary<string, string>()
            },
            "engineer-a");

        var started = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a",
            expectedRevision: project.Revision);

        Assert.Equal(ResearchProjectStatuses.Active, started.Status);
        Assert.Null(started.SiteCode);
        var visibleToOwner = await store.ListProjectsAsync("engineer-a", false, [], 50, 0);
        Assert.Contains(visibleToOwner, value => value.ProjectId == project.ProjectId);
    }

    [Fact]
    public async Task Project_CanCompleteAfterRealRunFlowsWithoutAnOperatingRegion()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "complete-real-run-flow" }, "engineer-a");
        var active = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a",
            expectedRevision: project.Revision);

        var completed = await workflow.ChangeProjectStatusAsync(
            active.ProjectId,
            ResearchProjectStatuses.Completed,
            "engineer-a",
            expectedRevision: active.Revision);

        Assert.Equal(ResearchProjectStatuses.Completed, completed.Status);
    }
}
