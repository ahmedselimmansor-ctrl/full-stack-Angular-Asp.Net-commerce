using Microsoft.Extensions.Caching.Hybrid;

namespace Sama.Shared.Infrastructure.Caching;

internal sealed class HybridSamaCache(HybridCache cache) : ISamaCache
{
    public ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CacheDuration duration,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync(
            key,
            factory,
            static (state, ct) => state(ct),
            ToEntryOptions(duration),
            tags,
            cancellationToken);

    public ValueTask SetAsync<T>(
        string key,
        T value,
        CacheDuration duration,
        IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default)
        => cache.SetAsync(key, value, ToEntryOptions(duration), tags, cancellationToken);

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        => cache.RemoveAsync(key, cancellationToken);

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        => cache.RemoveByTagAsync(tag, cancellationToken);

    private static HybridCacheEntryOptions ToEntryOptions(CacheDuration duration)
        => new() { Expiration = duration.Total, LocalCacheExpiration = duration.Local };
}
