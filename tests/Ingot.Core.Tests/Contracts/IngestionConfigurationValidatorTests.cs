// 验证采集配置共享契约中绑定归属对象的成对校验、规范化和兼容默认值。

using Ingot.Contracts.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class IngestionTaskBindingValidatorTests
{
    [Fact]
    public void BindingSubjectOverrideRequiresBothFieldsAndNormalizesValues()
    {
        var binding = Binding() with
        {
            SubjectType = " CAVITY ",
            SubjectId = " PRESS-01:CAVITY-2 ",
            EquipmentId = " PRESS-01 "
        };

        Assert.True(IngestionTaskValidator.TryValidateBinding(
            binding, out var normalized, out var errors), string.Join("；", errors));

        Assert.Equal("cavity", normalized!.SubjectType);
        Assert.Equal("PRESS-01:CAVITY-2", normalized.SubjectId);
        Assert.Equal("PRESS-01", normalized.EquipmentId);
    }

    [Theory]
    [InlineData("cavity", null, "PRESS-01")]
    [InlineData(null, "PRESS-01:CAVITY-2", "PRESS-01")]
    [InlineData(" ", " ", null)]
    [InlineData("invalid type", "PRESS-01:CAVITY-2", null)]
    [InlineData(null, null, " ")]
    public void BindingRejectsPartialBlankOrMalformedSubject(
        string? subjectType,
        string? subjectId,
        string? equipmentId)
    {
        Assert.False(IngestionTaskValidator.TryValidateBinding(
            Binding() with
            {
                SubjectType = subjectType,
                SubjectId = subjectId,
                EquipmentId = equipmentId
            },
            out _,
            out _));
    }

    private static IngestionTaskBinding Binding() => new()
    {
        TaskId = "press-cavity-2",
        Name = "Press cavity 2",
        TemplateId = "press-template",
        DataSourceId = "shared-press-source"
    };
}
