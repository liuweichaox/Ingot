// 统一映射 ApiProblemDetails 的 API 错误语义，避免端点自行拼装响应。

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
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
