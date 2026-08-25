using Microsoft.AspNetCore.Http;
using Sama.Shared.Kernel.Results;

namespace Sama.Shared.Infrastructure.Http;

/// <summary>
/// Maps a domain <see cref="Result"/> onto an HTTP response. Centralising this is what keeps
/// every endpoint from inventing its own status-code convention.
/// </summary>
public static class ResultExtensions
{
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)
            : result.Error.ToProblem();
    }

    public static IResult ToHttpResult(this Result result, IResult? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? onSuccess ?? Results.NoContent()
            : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Type.ToStatusCode();

        if (error.Type == ErrorType.Validation && error.Details is { Count: > 0 })
        {
            return Results.ValidationProblem(
                errors: error.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                detail: error.Message,
                title: "One or more validation errors occurred.",
                statusCode: status,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code });
        }

        return Results.Problem(
            detail: error.Message,
            statusCode: status,
            title: error.Type.ToTitle(),
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code });
    }

    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string ToTitle(this ErrorType type) => type switch
    {
        ErrorType.Validation => "Invalid request",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        ErrorType.TooManyRequests => "Too many requests",
        ErrorType.Unavailable => "Service unavailable",
        _ => "An unexpected error occurred",
    };
}
