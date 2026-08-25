namespace Sama.Shared.Infrastructure.RateLimiting;

/// <summary>Outcome of one rate-limit check, shaped to fill in the RateLimit-* response headers.</summary>
public readonly record struct RateLimitDecision(
    bool IsAllowed,
    int Limit,
    int Remaining,
    TimeSpan RetryAfter,
    TimeSpan ResetAfter)
{
    public static RateLimitDecision Allowed(int limit, int remaining, TimeSpan resetAfter)
        => new(true, limit, remaining, TimeSpan.Zero, resetAfter);

    public static RateLimitDecision Rejected(int limit, TimeSpan retryAfter)
        => new(false, limit, 0, retryAfter, retryAfter);
}

/// <summary>
/// Distributed rate limiter. It has to be distributed: an in-process limiter on N replicas
/// permits N times the configured rate, and the replica count changes under autoscaling.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Consumes one permit from <paramref name="policyName"/>'s bucket for
    /// <paramref name="partitionKey"/>. Consumes nothing when the request is rejected.
    /// </summary>
    ValueTask<RateLimitDecision> AcquireAsync(
        string policyName,
        string partitionKey,
        CancellationToken cancellationToken = default);
}
