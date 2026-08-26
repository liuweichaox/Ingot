// 验证 Agent 的 GoldenQuestionEvaluator 能力、只读边界和拒绝路径。

using System.Text.Json;
using Ingot.Contracts.Agents;
using Ingot.Platform.Infrastructure.Insight;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class GoldenQuestionEvaluatorTests
{
    [Fact]
    public void Evaluate_VerifiesToolFactReferenceAndAuditVersions()
    {
        var expected = JsonSerializer.SerializeToElement(0.95);
        var golden = Golden(expected);
        var run = Run("完整率为 0.95，属于可核查结果。", "sufficient");

        var result = new GoldenQuestionEvaluator().Evaluate(golden, run);

        Assert.True(result.Passed);
        Assert.All(result.Gates, static gate => Assert.True(gate.Passed, gate.Code));
        Assert.Equal("local-qwen", result.Model);
        Assert.Equal("ingot-chat-v1", result.PromptVersion);
        Assert.NotNull(result.AgentRunSnapshotHash);
        Assert.Equal(64, result.AgentRunSnapshotHash.Length);
        Assert.Equal(result.AgentRunSnapshotHash, GoldenQuestionEvaluator.SnapshotHash(run));
    }

    [Fact]
    public void Evaluate_FailsUnsupportedCausalClaim()
    {
        var golden = Golden(JsonSerializer.SerializeToElement(0.95));
        var run = Run("完整率为 0.95，温度导致缺陷。", "sufficient");

        var result = new GoldenQuestionEvaluator().Evaluate(golden, run);

        Assert.False(result.Passed);
        Assert.False(result.Gates.Single(static gate => gate.Code == "causal-guard").Passed);
    }

    [Fact]
    public void Evaluate_FailsStructuredCausalClaimWithoutKeywordMatch()
    {
        var golden = Golden(JsonSerializer.SerializeToElement(0.95));
        var run = Run("完整率为 0.95，形成一个可核查结论。", "sufficient") with
        {
            Answer = new AnalysisAnswer
            {
                Summary = "完整率为 0.95，形成一个可核查结论。",
                Findings =
                [
                    new AnalysisClaim
                    {
                        Statement = "温度变化解释了当前缺陷分布。",
                        Strength = AnalysisClaimStrengths.Causal,
                        EvidenceReferences = [Reference()]
                    }
                ],
                RelatedRecords = [Reference()]
            }
        };

        var result = new GoldenQuestionEvaluator().Evaluate(golden, run);

        Assert.False(result.Gates.Single(static gate => gate.Code == "causal-guard").Passed);
    }

    [Fact]
    public void Evaluate_FailsCausalClaimContainedOnlyInCombinedAnalysis()
    {
        var golden = Golden(JsonSerializer.SerializeToElement(0.95));
        var run = Run("完整率为 0.95，属于可核查结果。", "sufficient");
        run = run with
        {
            Answer = run.Answer! with
            {
                CombinedAnalysis = new CombinedAnalysisResult
                {
                    Status = "needs-review",
                    Summary = "需要复核。",
                    PossibleCauses =
                    [
                        new PossibleCause
                        {
                            CauseId = "h-1",
                            AuthorRole = AnalysisPerspectives.Process,
                            Statement = "温度导致缺陷。",
                            Reason = "需要复核。",
                            RelatedRecords = [Reference()]
                        }
                    ],
                    RelatedRecords = [Reference()]
                }
            }
        };

        var result = new GoldenQuestionEvaluator().Evaluate(golden, run);

        Assert.False(result.Gates.Single(static gate => gate.Code == "causal-guard").Passed);
    }

    [Fact]
    public void Evaluate_FailsAuditGateWhenToolResultContentWasTamperedAfterHashing()
    {
        var golden = Golden(JsonSerializer.SerializeToElement(0.95));
        var run = Run("完整率为 0.95，属于可核查结果。", "sufficient");
        run = run with
        {
            ToolResults =
            [
                run.ToolResults[0] with
                {
                    Data = JsonSerializer.SerializeToElement(new { completeness = 0.12 })
                }
            ]
        };

        var result = new GoldenQuestionEvaluator().Evaluate(golden, run);

        Assert.False(result.Gates.Single(static gate => gate.Code == "execution.auditable").Passed);
    }

    [Fact]
    public void Evaluate_RequiresToolBackedRefusal()
    {
        var golden = Golden(JsonSerializer.SerializeToElement(0.95)) with
        {
            ExpectedFacts = [],
            ExpectRefusal = true
        };
        var run = Run("数据不足，无法判断。", "insufficient-data") with
        {
            Answer = new AnalysisAnswer
            {
                Summary = "数据不足，无法判断。",
                Limitations = ["缺少检验记录。"],
                RelatedRecords = [Reference()]
            }
        };

        var result = new GoldenQuestionEvaluator().Evaluate(golden, run);

        Assert.True(result.Gates.Single(static gate => gate.Code == "refusal.correct").Passed);
    }

    [Fact]
    public void JsonPointer_HandlesEscapesAndArrays()
    {
        var data = JsonSerializer.SerializeToElement(new { values = new[] { new Dictionary<string, int> { ["a/b"] = 7 } } });
        Assert.True(GoldenQuestionEvaluator.TryResolvePointer(data, "/values/0/a~1b", out var value));
        Assert.Equal(7, value.GetInt32());
    }

    private static GoldenQuestionCase Golden(JsonElement expected) => new()
    {
        CaseId = Guid.CreateVersion7(),
        Version = 1,
        Name = "完整率核对",
        Question = "这批数据完整吗？",
        Status = GoldenQuestionStatuses.Reviewed,
        ReviewedBy = "engineer",
        ReviewedAt = DateTimeOffset.UtcNow,
        ExpectedFacts =
        [
            new GoldenExpectedFact
            {
                FactId = "completeness",
                Tool = "check_data_quality",
                JsonPointer = "/completeness",
                ExpectedValue = expected,
                AnswerMustContain = "0.95"
            }
        ],
        ExpectedRecordReferences = [Reference()]
    };

    private static AgentRunSnapshot Run(string summary, string outcome)
    {
        var toolResult = new AgentToolResultSnapshot
        {
            Tool = "check_data_quality",
            Version = "1.0.0",
            Summary = "完整率 0.95",
            Data = JsonSerializer.SerializeToElement(new { completeness = 0.95 }),
            RelatedRecords = [Reference()],
            Outcome = outcome,
            ContentHash = string.Empty,
            VerifiedAt = DateTimeOffset.UtcNow
        };
        toolResult = toolResult with { ContentHash = AgentToolResultIntegrity.ComputeContentHash(toolResult) };
        return new AgentRunSnapshot
        {
            RunId = "run-1",
            UserId = "operator",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Question = "这批数据完整吗？",
            Mode = "quick",
            Status = AgentRunStatuses.Completed,
            ModelProvider = "OpenAI",
            Model = "local-qwen",
            PromptVersion = "ingot-chat-v1",
            ToolsetVersion = "production-records-readonly-v2",
            CreatedAt = DateTimeOffset.UtcNow,
            Answer = new AnalysisAnswer
            {
                Summary = summary,
                RelatedRecords = [Reference()]
            },
            ToolResults = [toolResult],
            Usage = new AgentUsageSummary()
        };
    }

    private static RelatedRecordRef Reference() => new()
    {
        Kind = "event-query",
        Id = "batch-1",
        Label = "批次 1"
    };
}
