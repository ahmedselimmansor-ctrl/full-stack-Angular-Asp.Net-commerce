namespace Sama.Shared.Security.Authentication;

/// <summary>
/// Denylist consulted on every authenticated request. Short-lived access tokens make
/// revocation mostly unnecessary, but "log out everywhere" and "this account is
/// compromised" need to take effect now rather than in fifteen minutes.
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>True when this specific token was revoked (logout of one session).</summary>
    ValueTask<bool> IsTokenRevokedAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the token's security stamp no longer matches the user's current stamp —
    /// the "log out everywhere" case, triggered by a password change or an admin action.
    /// </summary>
    ValueTask<bool> IsStampStaleAsync(Guid userId, string securityStamp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fallback used by services that have no Redis binding. It fails <i>open</i>, so it is only
/// appropriate where the service is unreachable from outside the cluster; anything
/// internet-facing must register the Redis-backed store from Sama.Shared.Infrastructure.
/// </summary>
public sealed class NullTokenRevocationStore : ITokenRevocationStore
{
    public ValueTask<bool> IsTokenRevokedAsync(Guid tokenId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);

    public ValueTask<bool> IsStampStaleAsync(Guid userId, string securityStamp, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);
}
