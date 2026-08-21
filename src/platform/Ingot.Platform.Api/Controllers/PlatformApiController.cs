
using Ingot.Platform.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

public abstract class PlatformApiController : ControllerBase
{
    protected IActionResult InvalidRequest(
        string? detail,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status400BadRequest, detail, extensions);

    protected IActionResult AuthenticationRequired(
        string? detail = null,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status401Unauthorized, detail, extensions);

    protected IActionResult AuthorizationDenied(
        string? detail = null,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status403Forbidden, detail, extensions);

    protected IActionResult ResourceNotFound(
        string? detail = null,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status404NotFound, detail, extensions);

    protected IActionResult StateConflict(
        string? detail,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status409Conflict, detail, extensions);

    protected IActionResult PayloadTooLarge(
        string? detail,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status413PayloadTooLarge, detail, extensions);

    protected IActionResult UnprocessableRequest(
        string? detail,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status422UnprocessableEntity, detail, extensions);

    protected IActionResult RateLimited(
        string? detail,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status429TooManyRequests, detail, extensions);

    protected IActionResult ServiceUnavailable(
        string? detail,
        params (string Key, object? Value)[] extensions)
        => ProblemResponse(StatusCodes.Status503ServiceUnavailable, detail, extensions);

    protected IActionResult ServerFailure(string? detail = null)
        => ProblemResponse(StatusCodes.Status500InternalServerError, detail, []);

    protected ObjectResult ProblemResponse(
        int status,
        string? detail,
        IReadOnlyList<(string Key, object? Value)> extensions)
    {
        var problem = ApiProblemDetailsFactory.Create(HttpContext, status, detail);
        foreach (var (key, value) in extensions)
            problem.Extensions[key] = value;
        return new ObjectResult(problem)
        {
            StatusCode = status,
            DeclaredType = typeof(ApiProblemDetails),
            ContentTypes = { "application/problem+json" }
        };
    }
}
