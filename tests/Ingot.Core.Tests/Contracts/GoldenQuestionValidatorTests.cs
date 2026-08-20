// 验证共享契约 GoldenQuestionValidator 的合法输入、拒绝和兼容边界。

using Ingot.Contracts.Agents;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class GoldenQuestionValidatorTests
{
    [Fact]
    public void ReviewRequiresFactsAndRecordReferences()
    {
        var value = new GoldenQuestionCase
        {
            CaseId = Guid.CreateVersion7(),
            Name = "问题",
            Question = "为什么失败？"
        };

        Assert.False(GoldenQuestionValidator.TryValidate(value, true, out _, out var error));
        Assert.Contains("事实", error);
    }

    [Fact]
    public void DraftMayBeCollectedBeforeExpectedAnswerIsKnown()
    {
        var value = new GoldenQuestionCase
        {
            Name = "现场问题",
            Question = "为什么这次没有达到目标？"
        };

        Assert.True(GoldenQuestionValidator.TryValidate(value, false, out var normalized, out var error), error);
        Assert.NotEqual(Guid.Empty, normalized!.CaseId);
    }
}
