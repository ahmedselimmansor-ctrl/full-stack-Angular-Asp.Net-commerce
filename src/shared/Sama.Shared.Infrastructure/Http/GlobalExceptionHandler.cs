using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sama.Shared.Kernel.Results;

namespace Sama.Shared.Infrastructure.Http;

/// <summary>
/// Last-resort handler. Anything reaching here is a bug or an infrastructure failure, so the
/// response says as little as possible: a stack trace or a raw Npgsql message in the body
/// hands an attacker the schema, the framework version and often the query.
/// </summary>
public sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // A cancelled request is the client hanging up, not a server fault. Logging these at
        // Error level buries real incidents in noise during a traffic spike.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Request {Path} was cancelled by the client.", httpContext.Request.Path.Value);
            }

            return true;
        }

        var error = Classify(exception);

        if (logger.IsEnabled(LogLevel.Error))
        {
            logger.LogError(
                exception,
                "Unhandled {ExceptionType} on {Method} {Path} (traceId {TraceId}).",
                exception.GetType().Name,
                httpContext.Request.Method,
                httpContext.Request.Path,
                httpContext.TraceIdentifier);
        }

        var problem = error.ToProblem();

        httpContext.Response.StatusCode = error.Type.ToStatusCode();

        if (environment.IsDevelopment())
        {
            // Detail is safe to widen locally, and saves a round trip to the log stream.
            await Results.Problem(
                    detail: exception.ToString(),
                    statusCode: httpContext.Response.StatusCode,
                    title: error.Message,
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["code"] = error.Code,
                        ["traceId"] = httpContext.TraceIdentifier,
                    })
                .ExecuteAsync(httpContext);

            return true;
        }

        await problem.ExecuteAsync(httpContext);
        return true;
    }

    private static Error Classify(Exception exception) => exception switch
    {
        // 23505 unique_violation: a concurrent insert lost a race on a natural key.
        PostgresException { SqlState: "23505" } =>
            Error.Conflict("db.conflict", "That record already exists."),

        // 23503 foreign_key_violation: the request referenced something that is not there.
        PostgresException { SqlState: "23503" } =>
            Error.Validation("db.reference.invalid", "The request references a record that does not exist."),

        // 40001 serialization_failure / 40P01 deadlock_detected: safe for the client to retry.
        PostgresException { SqlState: "40001" or "40P01" } =>
            Error.Unavailable("db.retry", "The request conflicted with another operation. Please retry."),

        Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException =>
            Error.Conflict("db.concurrency", "This record was modified by someone else. Reload and try again."),

        TimeoutException or NpgsqlException =>
            Error.Unavailable("db.unavailable", "A dependency is temporarily unavailable. Please retry."),

        BadHttpRequestException =>
            Error.Validation("request.malformed", "The request could not be parsed."),

        _ => Error.Unexpected("server.unexpected", "An unexpected error occurred."),
    };
}
