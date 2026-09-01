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
}
