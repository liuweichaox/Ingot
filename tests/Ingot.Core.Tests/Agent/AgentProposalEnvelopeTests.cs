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

    [Fact]
    public void ProductionEvidenceProposal_IsAcceptedWithoutGrantingWriteAccess()
    {
        var proposal = Proposal() with
        {
            Kind = AgentProposalKinds.ProductionEvidence,
            Title = "关联后续真实生产运行",
            Rationale = "应在工程师决定后关联实际运行和质量记录，以形成可追溯的结果证据。"
        };

        var valid = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            Answer(proposal), [ToolResult()], out var error);

        Assert.True(valid, error);
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

    [Fact]
    public void CausalClaimIsRejectedEvenWithoutKeywordMatch()
    {
        var answer = Answer(Proposal()) with
        {
            Findings =
            [
                new AnalysisClaim
                {
                    Statement = "温度变化解释了当前缺陷分布。",
                    Strength = AnalysisClaimStrengths.Causal,
                    EvidenceReferences = [Reference()]
                }
            ]
        };

        var valid = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            answer, [ToolResult()], out var error);

        Assert.False(valid);
        Assert.Contains("因果结论", error);
    }

    [Fact]
    public void FindingCannotInventAnEvidenceReference()
    {
        var answer = Answer(Proposal()) with
        {
            Findings =
            [
                new AnalysisClaim
                {
                    Statement = "当前记录显示温度与缺陷同时变化。",
                    Strength = AnalysisClaimStrengths.Association,
                    EvidenceReferences =
                    [
                        new RelatedRecordRef
                        {
                            Kind = "process-execution",
                            Id = "execution-not-returned",
                            Label = "不存在的运行"
                        }
                    ]
                }
            ]
        };

        var valid = new DefaultAnalysisResultValidator().TryVerifyAnswer(
            answer, [ToolResult()], out var error);

        Assert.False(valid);
        Assert.Contains("每条分析发现", error);
    }

    private static AnalysisAnswer Answer(AgentProposalEnvelope proposal) => new()
    {
        Summary = "基于已读取运行形成一条待人工确认的下一配方建议。",
        Proposals = [proposal]
    };

    private static AgentProposalEnvelope Proposal() => new()
    {
        Kind = AgentProposalKinds.RecipeRecommendation,
        Title = "复核温度窗口",
        Rationale = "运行记录支持将该窗口作为下一配方候选，但尚不能视为因果结论。",
        DraftFields = new Dictionary<string, string>
        {
            ["stopRule"] = "出现安全约束或数据失效时停止。",
            ["rollbackPlan"] = "恢复经工程师确认的基线工艺规范。"
        },
        EvidenceReferences = [Reference()]
    };

    private static RelatedRecordRef Reference() => new()
    {
        Kind = "process-execution",
        Id = "execution-1",
        Label = "运行 execution-1"
    };

    private static AnalysisToolResult ToolResult()
    {
        using var document = JsonDocument.Parse("""{"status":"complete"}""");
        return new AnalysisToolResult
        {
            Tool = "get_process_execution_trace",
            Summary = "读取到一条完整运行。",
            Data = document.RootElement.Clone(),
            RelatedRecords = [Reference()]
        };
    }
}
