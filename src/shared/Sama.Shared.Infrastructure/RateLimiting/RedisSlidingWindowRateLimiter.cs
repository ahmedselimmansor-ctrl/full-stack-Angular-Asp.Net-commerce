using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Kernel.Time;
using StackExchange.Redis;

namespace Sama.Shared.Infrastructure.RateLimiting;

/// <summary>
/// Sliding-window-log limiter backed by a Redis sorted set, one entry per request.
/// <para>
/// The cheaper alternative — a fixed-window counter — allows a double burst across a window
/// boundary (a full window's worth at 11:59:59 and again at 12:00:00). For login throttling
/// that doubling is the whole attack, so the exact variant is worth its slightly higher cost.
/// Memory stays bounded because entries older than the window are trimmed on every call and
/// the key carries a TTL equal to the window.
/// </para>
/// </summary>
public sealed class RedisSlidingWindowRateLimiter(
    IRedisKeyspace keyspace,
    IOptions<RateLimitOptions> options,
    IClock clock,
    ILogger<RedisSlidingWindowRateLimiter> logger) : IRateLimiter
{
    /// <summary>
    /// Trim, count, and conditionally admit — as one atomic server-side step. Doing this as
    /// separate round trips would let concurrent requests all read the same under-limit count
    /// and all be admitted.
    /// <para>
    /// "Now" is supplied by the caller rather than read via Redis TIME so the script stays a
    /// pure function of its inputs; app replicas are NTP-synced within milliseconds, which is
    /// far tighter than any window we configure.
    /// </para>
    /// </summary>
    private const string SlidingWindowScript = """
        local key      = KEYS[1]
        local now      = tonumber(ARGV[1])
        local window   = tonumber(ARGV[2])
        local limit    = tonumber(ARGV[3])
        local member   = ARGV[4]

        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        local used = redis.call('ZCARD', key)

        if used < limit then
            redis.call('ZADD', key, now, member)
            redis.call('PEXPIRE', key, window)
            return { 1, limit - used - 1, 0, window }
        end

        -- Rejected: the window frees a slot when its oldest entry ages out.
        local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
        local retry = window
        if oldest[2] then
            retry = (tonumber(oldest[2]) + window) - now
            if retry < 0 then retry = 0 end
        end
        redis.call('PEXPIRE', key, window)
        return { 0, 0, retry, retry }
        """;

    public async ValueTask<RateLimitDecision> AcquireAsync(
        string policyName,
        string partitionKey,
        CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;

        if (!configuration.Enabled)
        {
            return RateLimitDecision.Allowed(int.MaxValue, int.MaxValue, TimeSpan.Zero);
        }

        if (!configuration.Policies.TryGetValue(policyName, out var policy))
        {
            // An endpoint naming a policy that was never configured must not silently become
            // unlimited - that is how a limit quietly disappears in a config refactor.
            throw new InvalidOperationException(
                $"Rate-limit policy '{policyName}' is not configured. Add it under '{RateLimitOptions.SectionName}:Policies'.");
        }

        var windowMs = policy.WindowSeconds * 1000L;
        var nowMs = clock.UtcNow.ToUnixTimeMilliseconds();
        var redisKey = keyspace.Key($"rl:{policyName}:{partitionKey}");

        // Unique per request, so two requests in the same millisecond are two set members
        // rather than one overwriting the other.
        var member = $"{nowMs:D}-{Guid.NewGuid():N}";

        try
        {
            var raw = await keyspace.Database.ScriptEvaluateAsync(
                SlidingWindowScript,
                [redisKey],
                [
                    nowMs.ToString(CultureInfo.InvariantCulture),
                    windowMs.ToString(CultureInfo.InvariantCulture),
                    policy.PermitLimit.ToString(CultureInfo.InvariantCulture),
                    member,
                ]).WaitAsync(cancellationToken);

            var result = (RedisValue[]?)raw;
            if (result is not { Length: 4 })
            {
                logger.LogWarning("Rate-limit script returned an unexpected shape for policy {Policy}.", policyName);
                return FallbackDecision(configuration, policy);
            }

            var allowed = (int)result[0] == 1;
            var remaining = (int)result[1];
            var retryAfterMs = (long)result[2];
            var resetMs = (long)result[3];

            return allowed
                ? RateLimitDecision.Allowed(policy.PermitLimit, remaining, TimeSpan.FromMilliseconds(resetMs))
                : RateLimitDecision.Rejected(policy.PermitLimit, TimeSpan.FromMilliseconds(retryAfterMs));
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or TimeoutException)
        {
            logger.LogError(ex,
                "Rate limiting is degraded for policy {Policy}; Redis is unavailable. FailOpen={FailOpen}.",
                policyName, configuration.FailOpen);

            return FallbackDecision(configuration, policy);
        }
    }

    private static RateLimitDecision FallbackDecision(RateLimitOptions configuration, RateLimitPolicy policy)
        => configuration.FailOpen
            ? RateLimitDecision.Allowed(policy.PermitLimit, 0, TimeSpan.Zero)
            : RateLimitDecision.Rejected(policy.PermitLimit, TimeSpan.FromSeconds(policy.WindowSeconds));
}
