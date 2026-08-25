namespace Sama.Shared.Infrastructure.Idempotency;

public enum IdempotencyState
{
    /// <summary>First time this key has been seen; the caller owns the operation and should proceed.</summary>
    Started,

    /// <summary>An identical request is running right now on another replica.</summary>
    InProgress,

    /// <summary>The operation already finished; replay the stored response.</summary>
    Completed,

    /// <summary>The key was reused with a different payload — almost always a client bug.</summary>
    PayloadMismatch,
}

public sealed record IdempotencyRecord(IdempotencyState State, int? StatusCode = null, string? Body = null);

/// <summary>
/// Backs the <c>Idempotency-Key</c> header on unsafe operations.
/// <para>
/// A phone on a flaky connection retries a checkout it never saw the response to. Without
/// this the customer is charged twice; with it the retry replays the original response.
/// </para>
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims <paramref name="key"/> atomically. <paramref name="requestFingerprint"/> is a
    /// hash of the request body, used to detect a key reused for a different request.
    /// </summary>
    ValueTask<IdempotencyRecord> BeginAsync(
        string key,
        string requestFingerprint,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    /// <summary>Stores the final response so later retries of the same key can replay it.</summary>
    ValueTask CompleteAsync(
        string key,
        int statusCode,
        string body,
        TimeSpan retention,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases a claim whose operation failed, so the client's retry is allowed to try again
    /// rather than being told an operation is in progress until the retention window expires.
    /// </summary>
    ValueTask AbandonAsync(string key, CancellationToken cancellationToken = default);
}
