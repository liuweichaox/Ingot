using Ingot.Platform.Api.Errors;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ApiArchitectureContractTests
{
    [Fact]
    public void ProblemDetails_PreservesCompatibilityAndMachineReadableIdentity()
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
        Assert.Equal(value.Detail, value.Error);
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
    public async Task ProblemFilter_ConvertsLegacyErrorObjectAndKeepsErrorAlias()
    {
        var http = new DefaultHttpContext();
        http.TraceIdentifier = "trace-filter";
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ResultExecutingContext(
            action,
            [],
            new BadRequestObjectResult(new { error = "请求无效。" }),
            new object());
        var filter = new ApiProblemDetailsResultFilter();

        await filter.OnResultExecutionAsync(
            context,
            () => Task.FromResult(new ResultExecutedContext(
                action,
                [],
                context.Result,
                new object())));

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var problem = Assert.IsType<ApiProblemDetails>(result.Value);
        Assert.Equal("request.invalid", problem.Code);
        Assert.Equal("请求无效。", problem.Error);
        Assert.Contains("application/problem+json", result.ContentTypes);
    }
}
