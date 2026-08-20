// 验证边缘组件 AcquisitionSecretReference 的协议、状态和失败边界。

using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionSecretReferenceTests
{
    [Fact]
    public void ConfiguredButMissingSecretFailsClosed()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AcquisitionSecretReference.ResolveOptional(new MissingSecrets(), "device-password", "设备密码"));

        Assert.Contains("device-password", error.Message);
    }

    private sealed class MissingSecrets : IAcquisitionSecretResolver
    {
        public string? Resolve(string? reference) => null;
    }
}
