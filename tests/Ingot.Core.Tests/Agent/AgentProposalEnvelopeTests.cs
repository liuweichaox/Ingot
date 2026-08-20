// 验证 Agent 的 AgentProposalEnvelope 能力、只读边界和拒绝路径。

using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class AgentProposalEnvelopeTests
{
    [Fact]
    public void EvidenceGroundedPreviewProposal_IsAcceptedWithoutGrantingWriteAccess()
    {
        var result = ToolResult();
        var answer = Answer(Proposal());

        var valid = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            answer, [result], out var error);

        Assert.True(valid, error);
        Assert.Equal("preview-only", Assert.Single(answer.Proposals).Persistence);
        Assert.True(answer.Proposals[0].RequiresHumanConfirmation);
    }

    [Theory]
    [InlineData("persisted", true)]
    [InlineData("preview-only", false)]
    public void ProposalThatBypassesHumanConfirmation_IsRejected(
        string persistence,
        bool requiresHumanConfirmation)
    {
        var proposal = Proposal() with
        {
            Persistence = persistence,
            RequiresHumanConfirmation = requiresHumanConfirmation
        };

        var valid = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            Answer(proposal), [ToolResult()], out var error);

        Assert.False(valid);
        Assert.Contains("人工确认", error);
    }

    [Fact]
    public void ProposalCannotInventAnEvidenceReference()
    {
        var proposal = Proposal() with
        {
            EvidenceReferences =
            [
                new RelatedRecordRef
                {
                    Kind = "process-execution",
                    Id = "execution-not-returned",
                    Label = "不存在的运行"
                }
            ]
        };

        var valid = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            Answer(proposal), [ToolResult()], out var error);

        Assert.False(valid);
        Assert.Contains("只读工具", error);
    }

    private static AnalysisAnswer Answer(AgentProposalEnvelope proposal) => new()
    {
        Summary = "基于已读取运行形成一个待人工确认的实验草案。",
        Proposals = [proposal]
    };

    private static AgentProposalEnvelope Proposal() => new()
    {
        Kind = AgentProposalKinds.Experiment,
        Title = "复核温度窗口",
        Rationale = "运行记录显示该窗口值得通过受控实验复核，但尚不能视为因果结论。",
        DraftFields = new Dictionary<string, string>
        {
            ["stopRule"] = "出现安全约束或数据失效时停止。",
            ["rollbackPlan"] = "恢复经工程师确认的基线工艺规范。"
        },
        EvidenceReferences =
        [
            new RelatedRecordRef
            {
                Kind = "process-execution",
                Id = "execution-1",
                Label = "运行 execution-1"
            }
        ]
    };

    private static AnalysisToolResult ToolResult()
    {
        using var document = JsonDocument.Parse("""{"status":"complete"}""");
        return new AnalysisToolResult
        {
            Tool = "get_process_execution_trace",
            Summary = "读取到一条完整运行。",
            Data = document.RootElement.Clone(),
            RelatedRecords =
            [
                new RelatedRecordRef
                {
                    Kind = "process-execution",
                    Id = "execution-1",
                    Label = "运行 execution-1"
                }
            ]
        };
    }
}
