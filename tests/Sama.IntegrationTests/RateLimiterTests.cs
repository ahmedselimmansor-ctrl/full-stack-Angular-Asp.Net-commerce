using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sama.Shared.Infrastructure.RateLimiting;
using Shouldly;

namespace Sama.IntegrationTests;

/// <summary>
/// The distributed rate limiter, against real Redis.
///
/// The claims worth proving are that the Lua script is atomic under concurrency, and that the
/// window genuinely slides — a fixed-window counter would pass a naive test while allowing a
/// double burst across the boundary, which for login throttling is the whole attack.
/// </summary>
[Collection(SamaCollection.Name)]
public class RateLimiterTests(SamaFixture fixture)
{
    private const string Policy = "test";

    private RedisSlidingWindowRateLimiter Build(int permitLimit, int windowSeconds, bool failOpen = true)
    {
        var options = new RateLimitOptions { Enabled = true, FailOpen = failOpen };
        options.Policies[Policy] = new RateLimitPolicy
        {
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            Partition = RateLimitPartition.User,
        };

        return new RedisSlidingWindowRateLimiter(
            fixture.Keyspace,
            Options.Create(options),
            fixture.Clock,
            NullLogger<RedisSlidingWindowRateLimiter>.Instance);
    }

    [Fact]
    public async Task Requests_are_allowed_up_to_the_limit_and_refused_after_it()
    {
        await fixture.ResetRedisAsync();
        var limiter = Build(permitLimit: 3, windowSeconds: 60);
        var key = $"u:{Guid.NewGuid():N}";

        for (var i = 1; i <= 3; i++)
        {
            var allowed = await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);
            allowed.IsAllowed.ShouldBeTrue($"request {i} is within the limit");
            allowed.Remaining.ShouldBe(3 - i);
        }

        var refused = await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);
        refused.IsAllowed.ShouldBeFalse();
        refused.RetryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task A_refused_request_does_not_consume_a_permit()
    {
        // Otherwise a client hammering a limit would push its own reset further out on every
        // rejected attempt, turning a short throttle into an indefinite lockout.
        await fixture.ResetRedisAsync();
        var limiter = Build(permitLimit: 2, windowSeconds: 60);
        var key = $"u:{Guid.NewGuid():N}";

        await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);
        await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);

        var first = await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);
        var second = await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);

        first.IsAllowed.ShouldBeFalse();
        second.RetryAfter.ShouldBeLessThanOrEqualTo(first.RetryAfter);
    }

    [Fact]
    public async Task Buckets_are_independent_per_partition_key()
    {
        await fixture.ResetRedisAsync();
        var limiter = Build(permitLimit: 1, windowSeconds: 60);

        (await limiter.AcquireAsync(Policy, "u:alice", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await limiter.AcquireAsync(Policy, "u:bob", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await limiter.AcquireAsync(Policy, "u:alice", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_window_slides_rather_than_resetting_on_a_boundary()
    {
        // Three requests, then time advances past half the window. A fixed-window counter
        // would have reset by now and allowed a full fresh burst; a sliding window only frees
        // the permits whose entries have actually aged out.
        await fixture.ResetRedisAsync();
        var limiter = Build(permitLimit: 3, windowSeconds: 60);
        var key = $"u:{Guid.NewGuid():N}";

        for (var i = 0; i < 3; i++)
        {
            await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken);
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        (await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken)).IsAllowed
            .ShouldBeFalse("the earlier requests are still inside the window");

        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        (await limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken)).IsAllowed
            .ShouldBeTrue("the earlier requests have now aged out");
    }

    [Fact]
    public async Task Concurrent_requests_cannot_exceed_the_limit()
    {
        // The point of doing trim-count-admit in one Lua script. Separate round trips would
        // let every one of these read the same under-limit count and all be admitted.
        await fixture.ResetRedisAsync();
        var limiter = Build(permitLimit: 10, windowSeconds: 60);
        var key = $"u:{Guid.NewGuid():N}";

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ =>
                limiter.AcquireAsync(Policy, key, TestContext.Current.CancellationToken).AsTask()));

        attempts.Count(a => a.IsAllowed).ShouldBe(10);
    }

    [Fact]
    public async Task An_unconfigured_policy_throws_rather_than_becoming_unlimited()
    {
        // A limit that quietly disappears in a config refactor is worse than a loud failure.
        await fixture.ResetRedisAsync();
        var limiter = Build(permitLimit: 5, windowSeconds: 60);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await limiter.AcquireAsync("no-such-policy", "u:x", TestContext.Current.CancellationToken));
    }
}
