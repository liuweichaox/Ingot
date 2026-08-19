using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;

namespace Ingot.Platform.Api.Errors;

public sealed class ApiProblemDetails : ProblemDetails
{
    public required string Code { get; init; }
    public required string TraceId { get; init; }
}

public static class ApiProblemDetailsFactory
{
    public static ApiProblemDetails Create(HttpContext context, int status, string? detail = null)
    {
        var code = CodeFor(status);
        detail = string.IsNullOrWhiteSpace(detail)
            ? ReasonPhrases.GetReasonPhrase(status)
            : detail.Trim();
        return new ApiProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
            Type = $"urn:ingot:problem:{code}",
            Instance = context.Request.Path,
            Code = code,
            TraceId = Activity.Current?.Id ?? context.TraceIdentifier
        };
    }

    public static string CodeFor(int status)
        => status switch
        {
            StatusCodes.Status400BadRequest => "request.invalid",
            StatusCodes.Status401Unauthorized => "auth.required",
            StatusCodes.Status403Forbidden => "auth.forbidden",
            StatusCodes.Status404NotFound => "resource.not-found",
            StatusCodes.Status409Conflict => "state.conflict",
            StatusCodes.Status413PayloadTooLarge => "request.too-large",
            StatusCodes.Status422UnprocessableEntity => "request.unprocessable",
            StatusCodes.Status429TooManyRequests => "request.rate-limited",
            StatusCodes.Status503ServiceUnavailable => "service.unavailable",
            _ when status >= 500 => "server.error",
            _ => "request.failed"
        };
}

public sealed class ApiProblemDetailsResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: not ProblemDetails } result &&
            (result.StatusCode ?? 0) is >= 400 and <= 599 &&
            TryReadError(result.Value, out var detail, out var errors))
        {
            var problem = ApiProblemDetailsFactory.Create(
                context.HttpContext,
                result.StatusCode!.Value,
                detail);
            if (errors is not null)
                problem.Extensions["errors"] = errors;
            result.Value = problem;
            result.DeclaredType = typeof(ApiProblemDetails);
            result.ContentTypes.Clear();
            result.ContentTypes.Add("application/problem+json");
        }
        await next().ConfigureAwait(false);
    }

    private static bool TryReadError(object? value, out string? detail, out object? errors)
    {
        detail = null;
        errors = null;
        if (value is null) return false;
        var type = value.GetType();
        detail = type.GetProperty("error")?.GetValue(value)?.ToString()
                 ?? type.GetProperty("Error")?.GetValue(value)?.ToString();
        errors = type.GetProperty("errors")?.GetValue(value)
                 ?? type.GetProperty("Errors")?.GetValue(value);
        return detail is not null;
    }
}

public sealed class ApiProblemDetailsConvention : IApplicationModelConvention
{
    private static readonly int[] CommonErrorStatuses =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status500InternalServerError,
        StatusCodes.Status503ServiceUnavailable
    ];

    public void Apply(ApplicationModel application)
    {
        foreach (var action in application.Controllers.SelectMany(static value => value.Actions))
        foreach (var status in CommonErrorStatuses)
        {
            if (action.Filters.OfType<ProducesResponseTypeAttribute>()
                .Any(value => value.StatusCode == status))
                continue;
            action.Filters.Add(new ProducesResponseTypeAttribute(typeof(ApiProblemDetails), status));
        }
    }
}
