using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sama.Basket.Domain;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Kernel;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Kernel.Time;
using StackExchange.Redis;

namespace Sama.Basket.Storage;

public sealed class BasketOptions
{
    public const string SectionName = "Basket";

    /// <summary>How long a signed-in shopper's basket survives inactivity.</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;

    /// <summary>Guest baskets expire sooner; they are far more numerous and rarely returned to.</summary>
    [Range(1, 90)]
    public int GuestRetentionDays { get; set; } = 7;

    /// <summary>Caps a single line. Bulk buyers go through a quote, not the storefront basket.</summary>
    [Range(1, 1000)]
    public int MaxQuantityPerLine { get; set; } = 20;

    /// <summary>Caps distinct lines, bounding both the Redis document and the checkout payload.</summary>
    [Range(1, 500)]
    public int MaxLines { get; set; } = 100;

    /// <summary>Attempts before a concurrent-write conflict is reported to the caller.</summary>
    [Range(1, 10)]
    public int MaxSaveAttempts { get; set; } = 4;
}

/// <summary>
/// Redis-backed basket storage with optimistic concurrency.
/// </summary>
public sealed class BasketStore(
    IRedisKeyspace keyspace,
    IOptions<BasketOptions> options,
    IClock clock)
{
    private readonly BasketOptions _options = options.Value;

    /// <summary>
    /// Compare-and-set. Read-modify-write over two round trips would let a shopper's phone and
    /// laptop each read version 4, both write version 5, and silently lose one of the edits.
    /// Rejecting the stale write lets the caller re-read and retry.
    /// </summary>
    private const string SaveScript = """
        local key      = KEYS[1]
        local expected = ARGV[1]
        local nextVer  = ARGV[2]
        local document = ARGV[3]
        local ttl      = tonumber(ARGV[4])

        local current = redis.call('HGET', key, 'version')
        if current == false then current = '0' end

        if current ~= expected then
            return { 0, current }
        end

        redis.call('HSET', key, 'version', nextVer, 'doc', document)
        redis.call('PEXPIRE', key, ttl)
        return { 1, nextVer }
        """;

    public async Task<CustomerBasket> GetAsync(Guid tenantId, Guid ownerId, CancellationToken cancellationToken)
    {
        var entries = await keyspace.Database
            .HashGetAllAsync(Key(tenantId, ownerId))
            .WaitAsync(cancellationToken);

        if (entries.Length == 0)
        {
            return CustomerBasket.Empty(tenantId, ownerId, clock.UtcNow);
        }

        var document = entries.FirstOrDefault(e => e.Name == "doc").Value;
        var version = entries.FirstOrDefault(e => e.Name == "version").Value;

        if (!document.HasValue)
        {
            return CustomerBasket.Empty(tenantId, ownerId, clock.UtcNow);
        }

        // Explicit casts: RedisValue converts implicitly to both string and ReadOnlySpan<byte>,
        // which makes the overload ambiguous without one.
        var basket = JsonSerializer.Deserialize<CustomerBasket>((string)document!, SamaJson.Default)
                     ?? CustomerBasket.Empty(tenantId, ownerId, clock.UtcNow);

        basket.Version = long.TryParse((string?)version, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        return basket;
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to the basket and persists it, retrying from a fresh
    /// read when another writer wins the race.
    /// </summary>
    public async Task<Result<CustomerBasket>> MutateAsync(
        Guid tenantId,
        Guid ownerId,
        bool isGuest,
        Func<CustomerBasket, Result> mutate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        for (var attempt = 0; attempt < _options.MaxSaveAttempts; attempt++)
        {
            var basket = await GetAsync(tenantId, ownerId, cancellationToken);

            var outcome = mutate(basket);
            if (outcome.IsFailure)
            {
                return outcome.Error;
            }

            if (basket.Items.Count > _options.MaxLines)
            {
                return Error.Validation(
                    "basket.tooManyLines",
                    $"A basket may hold at most {_options.MaxLines} different products.");
            }

            basket.UpdatedAt = clock.UtcNow;

            if (await TrySaveAsync(basket, isGuest, cancellationToken))
            {
                return basket;
            }
        }

        return Error.Conflict(
            "basket.concurrentUpdate",
            "The basket was changed elsewhere while this update was in flight. Reload and try again.");
    }

    private async Task<bool> TrySaveAsync(CustomerBasket basket, bool isGuest, CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromDays(isGuest ? _options.GuestRetentionDays : _options.RetentionDays);
        var next = basket.Version + 1;

        var document = JsonSerializer.Serialize(basket with { Version = next }, SamaJson.Default);

        var raw = await keyspace.Database.ScriptEvaluateAsync(
            SaveScript,
            [Key(basket.TenantId, basket.OwnerId)],
            [
                basket.Version.ToString(CultureInfo.InvariantCulture),
                next.ToString(CultureInfo.InvariantCulture),
                document,
                ((long)ttl.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            ]).WaitAsync(cancellationToken);

        var result = (RedisValue[]?)raw;
        if (result is not { Length: 2 } || (int)result[0] != 1)
        {
            return false;
        }

        basket.Version = next;
        return true;
    }

    public async Task ClearAsync(Guid tenantId, Guid ownerId, CancellationToken cancellationToken)
        => await keyspace.Database.KeyDeleteAsync(Key(tenantId, ownerId)).WaitAsync(cancellationToken);

    /// <summary>
    /// Merges a guest basket into the signed-in one, then discards the guest copy. Called after
    /// login so a shopper who filled a basket before signing in does not lose it.
    /// </summary>
    public async Task<Result<CustomerBasket>> MergeAsync(
        Guid tenantId,
        Guid guestId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var guest = await GetAsync(tenantId, guestId, cancellationToken);
        if (guest.Items.Count == 0)
        {
            return await GetAsync(tenantId, userId, cancellationToken);
        }

        var merged = await MutateAsync(tenantId, userId, isGuest: false, basket =>
        {
            foreach (var item in guest.Items)
            {
                basket.AddOrIncrement(item, _options.MaxQuantityPerLine);
            }

            return Result.Success();
        }, cancellationToken);

        if (merged.IsSuccess)
        {
            await ClearAsync(tenantId, guestId, cancellationToken);
        }

        return merged;
    }

    private string Key(Guid tenantId, Guid ownerId)
        => keyspace.Key($"basket:{tenantId:N}:{ownerId:N}");
}
