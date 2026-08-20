// 验证边缘组件 JsonElementPathResolver 的协议、状态和失败边界。

using System.Text.Json;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class JsonElementPathResolverTests
{
    [Theory]
    [InlineData("items[1].value")]
    [InlineData("/items/1/value")]
    public void ResolvesArrayPaths(string path)
    {
        using var document = JsonDocument.Parse("""{"items":[{"value":1},{"value":2}]}""");
        Assert.True(JsonElementPathResolver.TryResolve(document.RootElement, path, out var value));
        Assert.Equal(2, value.GetInt32());
    }

    [Fact]
    public void JsonPointerResolvesPropertyNamesContainingDotsAndSlashes()
    {
        using var document = JsonDocument.Parse("""{"a.b":{"c/d":3}}""");
        Assert.True(JsonElementPathResolver.TryResolve(document.RootElement, "/a.b/c~1d", out var value));
        Assert.Equal(3, value.GetInt32());
    }
}
