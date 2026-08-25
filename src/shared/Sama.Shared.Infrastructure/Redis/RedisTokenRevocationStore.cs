using Sama.Shared.Security.Authentication;
using StackExchange.Redis;

namespace Sama.Shared.Infrastructure.Redis;

/// <summary>
/// Redis-backed denylist. Entries are written with a TTL equal to the remaining lifetime of
/// the token they revoke, so the list stays small — a revoked token that has already expired
/// cannot be used anyway.
/// </summary>
public sealed class RedisTokenRevocationStore(IRedisKeyspace keyspace) : ITokenRevocationStore
{
    public async ValueTask<bool> IsTokenRevokedAsync(Guid tokenId, CancellationToken cancellationToken = default)
        => await keyspace.Database
            .KeyExistsAsync(RevokedTokenKey(keyspace, tokenId))
            .WaitAsync(cancellationToken);

    public async ValueTask<bool> IsStampStaleAsync(
        Guid userId,
        string securityStamp,
        CancellationToken cancellationToken = default)
    {
        var current = await keyspace.Database
            .StringGetAsync(StampKey(keyspace, userId))
            .WaitAsync(cancellationToken);

        // No cached stamp means nothing has revoked this user's sessions; treat as current.
        // The authoritative stamp lives in Identity's database and is pushed here on change.
        return current.HasValue && !string.Equals(current.ToString(), securityStamp, StringComparison.Ordinal);
    }

    /// <summary>Revokes one token until it would have expired anyway.</summary>
    public async ValueTask RevokeTokenAsync(
        Guid tokenId,
        TimeSpan remainingLifetime,
        CancellationToken cancellationToken = default)
    {
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return;
        }

        await keyspace.Database
            .StringSetAsync(RevokedTokenKey(keyspace, tokenId), "1", remainingLifetime)
            .WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Publishes a new security stamp, invalidating every access token the user currently
    /// holds. Called on password change, on "sign out everywhere", and on admin lockout.
    /// </summary>
    public async ValueTask SetSecurityStampAsync(
        Guid userId,
        string securityStamp,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
        => await keyspace.Database
            .StringSetAsync(StampKey(keyspace, userId), securityStamp, retention)
            .WaitAsync(cancellationToken);

    private static RedisKey RevokedTokenKey(IRedisKeyspace keyspace, Guid tokenId)
        => keyspace.Key("revoked:jti:" + tokenId.ToString("N"));

    private static RedisKey StampKey(IRedisKeyspace keyspace, Guid userId)
        => keyspace.Key("revoked:stamp:" + userId.ToString("N"));
}
