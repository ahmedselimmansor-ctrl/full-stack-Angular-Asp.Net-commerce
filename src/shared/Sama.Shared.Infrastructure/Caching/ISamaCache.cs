namespace Sama.Shared.Infrastructure.Caching;

/// <summary>
/// Two-tier cache: an in-process L1 in front of a shared Redis L2.
/// <para>
/// L1 absorbs the repeat reads a single pod makes within a few seconds without touching the
/// network; L2 keeps a newly-scaled-up pod from arriving with a cold cache and stampeding
/// the database. Both tiers are coordinated by <c>HybridCache</c>, which also collapses
/// concurrent misses for the same key into one factory call.
/// </para>
/// </summary>
public interface ISamaCache
{
    /// <summary>
    /// Returns the cached value, or invokes <paramref name="factory"/> once and caches it.
    /// Concurrent callers for the same key wait on that single call rather than each running
    /// their own — the difference between one database query and a hundred when a hot key expires.
    /// </summary>
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheDuration duration,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(
        string key,
        T value,
        CacheDuration duration,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every entry carrying <paramref name="tag"/>. Writes invalidate by tag because a
    /// price change has to evict the product, its category listing, and any search page it
    /// appears on — enumerating those keys at write time is not tractable.
    /// </summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
}

/// <summary>
/// How long an entry lives in each tier. L1 is deliberately much shorter than L2: it is
/// per-pod and cannot be invalidated across the fleet, so a stale L1 entry is the one thing
/// tag invalidation cannot immediately fix.
/// </summary>
public readonly record struct CacheDuration(TimeSpan Total, TimeSpan Local)
{
    public static CacheDuration Brief { get; } = new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));

    public static CacheDuration Standard { get; } = new(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30));

    public static CacheDuration Extended { get; } = new(TimeSpan.FromHours(1), TimeSpan.FromMinutes(2));

    public static CacheDuration Of(TimeSpan total)
        => new(total, total < TimeSpan.FromMinutes(1) ? total : TimeSpan.FromSeconds(30));
}
