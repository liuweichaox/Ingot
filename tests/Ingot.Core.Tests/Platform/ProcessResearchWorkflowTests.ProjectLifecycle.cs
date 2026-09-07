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
    public async Task Project_RequiresAnExplicitProductionScopeBeforeActivation()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "scope-required", Context = new Dictionary<string, string>() },
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() => workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a",
            expectedRevision: project.Revision));

        Assert.Contains("至少绑定一个产品", error.Message, StringComparison.Ordinal);
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
