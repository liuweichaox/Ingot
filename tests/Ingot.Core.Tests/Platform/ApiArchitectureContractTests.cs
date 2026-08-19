using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Api.Errors;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ApiArchitectureContractTests
{
    [Fact]
    public void ProblemDetails_ProvidesMachineReadableIdentity()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-test";
        context.Request.Path = "/api/v1/example";

        var value = ApiProblemDetailsFactory.Create(
            context,
            StatusCodes.Status409Conflict,
            "状态已经变化。");

        Assert.Equal("state.conflict", value.Code);
        Assert.Equal("trace-test", value.TraceId);
        Assert.Equal("urn:ingot:problem:state.conflict", value.Type);
    }

    [Fact]
    public void ResearchCursor_RoundTripsAndRejectsMalformedInput()
    {
        var timestamp = DateTimeOffset.Parse("2026-08-19T12:34:56.1234567Z");
        var id = Guid.CreateVersion7();

        var cursor = ResearchPageCursor.Encode(timestamp, id);

        Assert.True(ResearchPageCursor.TryDecode(cursor, out var decodedTimestamp, out var decodedId));
        Assert.Equal(timestamp, decodedTimestamp);
        Assert.Equal(id, decodedId);
        Assert.False(ResearchPageCursor.TryDecode("not-a-cursor", out _, out _));
    }

    [Fact]
    public void PlatformController_ReturnsProblemDetailsDirectly()
    {
        var http = new DefaultHttpContext();
        http.TraceIdentifier = "trace-controller";
        var controller = new TestPlatformController
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var result = Assert.IsType<ObjectResult>(controller.Invalid("请求无效。"));
        var problem = Assert.IsType<ApiProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("request.invalid", problem.Code);
        Assert.Equal("请求无效。", problem.Detail);
        Assert.Contains("application/problem+json", result.ContentTypes);
    }

    private sealed class TestPlatformController : PlatformApiController
    {
        public IActionResult Invalid(string detail) => InvalidRequest(detail);
    }
}
