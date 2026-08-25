using Sama.Shared.Infrastructure.Idempotency;
using Shouldly;

namespace Sama.IntegrationTests;

/// <summary>
/// The idempotency store, against real Redis.
///
/// This is what stands between a phone retrying a checkout it never saw the response to and
/// the customer being charged twice, so the concurrency test is the one that matters.
/// </summary>
[Collection(SamaCollection.Name)]
public class IdempotencyTests(SamaFixture fixture)
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RedisIdempotencyStore Store() => new(fixture.Keyspace);

    [Fact]
    public async Task The_first_claim_on_a_key_starts_the_operation()
    {
        await fixture.ResetRedisAsync();

        var record = await Store().BeginAsync($"key-{Guid.NewGuid():N}", "fingerprint", Retention, Ct);

        record.State.ShouldBe(IdempotencyState.Started);
    }

    [Fact]
    public async Task A_second_claim_while_the_first_is_running_reports_it_as_in_progress()
    {
        await fixture.ResetRedisAsync();
        var store = Store();
        var key = $"key-{Guid.NewGuid():N}";

        await store.BeginAsync(key, "fingerprint", Retention, Ct);
        var second = await store.BeginAsync(key, "fingerprint", Retention, Ct);

        second.State.ShouldBe(IdempotencyState.InProgress);
    }

    [Fact]
    public async Task A_completed_operation_replays_its_original_response()
    {
        await fixture.ResetRedisAsync();
        var store = Store();
        var key = $"key-{Guid.NewGuid():N}";

        await store.BeginAsync(key, "fingerprint", Retention, Ct);
        await store.CompleteAsync(key, 202, """{"orderNumber":"SAMA-1"}""", Retention, Ct);

        var replay = await store.BeginAsync(key, "fingerprint", Retention, Ct);

        replay.State.ShouldBe(IdempotencyState.Completed);
        replay.StatusCode.ShouldBe(202);
        replay.Body.ShouldNotBeNull();
        replay.Body!.ShouldContain("SAMA-1");
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_payload_is_reported_as_a_mismatch()
    {
        // Almost always a client bug. Silently replaying the first response would deliver the
        // wrong order to a request that asked for something else.
        await fixture.ResetRedisAsync();
        var store = Store();
        var key = $"key-{Guid.NewGuid():N}";

        await store.BeginAsync(key, "fingerprint-a", Retention, Ct);
        var mismatch = await store.BeginAsync(key, "fingerprint-b", Retention, Ct);

        mismatch.State.ShouldBe(IdempotencyState.PayloadMismatch);
    }

    [Fact]
    public async Task Exactly_one_of_many_concurrent_claims_wins()
    {
        // The reason claim-or-report is a single Lua script. A GET-then-SET would let two
        // concurrent retries both see "absent" and both place an order.
        await fixture.ResetRedisAsync();
        var store = Store();
        var key = $"key-{Guid.NewGuid():N}";

        var claims = await Task.WhenAll(
            Enumerable.Range(0, 25).Select(_ =>
                store.BeginAsync(key, "fingerprint", Retention, Ct).AsTask()));

        claims.Count(c => c.State == IdempotencyState.Started).ShouldBe(1);
        claims.Count(c => c.State == IdempotencyState.InProgress).ShouldBe(24);
    }

    [Fact]
    public async Task Abandoning_a_failed_operation_lets_the_client_retry()
    {
        // Without this, a checkout that failed for an unrelated reason would be told an
        // operation is in progress for the next 24 hours.
        await fixture.ResetRedisAsync();
        var store = Store();
        var key = $"key-{Guid.NewGuid():N}";

        await store.BeginAsync(key, "fingerprint", Retention, Ct);
        await store.AbandonAsync(key, Ct);

        (await store.BeginAsync(key, "fingerprint", Retention, Ct)).State
            .ShouldBe(IdempotencyState.Started);
    }
}
