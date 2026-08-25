using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sama.Identity.Domain;
using Sama.Identity.Features.Keys;
using Sama.Identity.Persistence;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Kernel.Results;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Permissions;

namespace Sama.Identity.Features.Auth;

public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

/// <summary>
/// Mints access tokens and manages the refresh-token rotation chain.
/// </summary>
public sealed class TokenService(
    SigningKeyProvider keys,
    IOptions<SigningOptions> signingOptions,
    IdentityDbContext database,
    RedisTokenRevocationStore revocations,
    IClock clock)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();
    private readonly SigningOptions _options = signingOptions.Value;

    /// <summary>
    /// Issues a fresh access/refresh pair. <paramref name="familyId"/> continues an existing
    /// rotation chain on refresh, or starts a new one on login.
    /// </summary>
    public async Task<IssuedTokens> IssueAsync(
        User user,
        string? clientIp,
        string? userAgent,
        Guid? familyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;
        var accessToken = CreateAccessToken(user, now);

        var rawRefreshToken = RefreshToken.GenerateRawToken();
        var refreshToken = RefreshToken.Issue(
            user.Id,
            user.TenantId,
            familyId ?? Guid.CreateVersion7(),
            rawRefreshToken,
            now,
            TimeSpan.FromDays(_options.RefreshTokenDays),
            clientIp,
            userAgent);

        database.RefreshTokens.Add(refreshToken);
        await database.SaveChangesAsync(cancellationToken);

        // Republish the stamp so resource services can reject tokens issued before the last
        // credential or grant change.
        await PublishSecurityStampAsync(user, cancellationToken);

        return new IssuedTokens(accessToken, rawRefreshToken, _options.AccessTokenMinutes * 60);
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the old one out.
    /// <para>
    /// Presenting an already-consumed token is treated as theft: the entire family is revoked,
    /// which logs out both the attacker and the legitimate holder. That is the intended
    /// outcome — a forced re-login is a far better result than an attacker holding a
    /// self-renewing session indefinitely.
    /// </para>
    /// </summary>
    public async Task<Result<IssuedTokens>> RefreshAsync(
        string rawRefreshToken,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = RefreshToken.Hash(rawRefreshToken);

        var token = await database.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            return Error.Unauthorized("auth.refresh.invalid", "The refresh token is not valid.");
        }

        if (token.ConsumedAt is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, now, "refresh token reuse detected", cancellationToken);

            return Error.Unauthorized(
                "auth.refresh.reused",
                "This session has been terminated for security reasons. Please sign in again.");
        }

        if (!token.IsActive(now))
        {
            return Error.Unauthorized("auth.refresh.expired", "The refresh token has expired or been revoked.");
        }

        // IgnoreQueryFilters: a refresh call carries no bearer token, so there is no ambient
        // tenant and the model-level filter would match zero rows. The tenant is instead
        // pinned to the one recorded on the token itself, which is server-issued and therefore
        // not attacker-chosen.
        var user = await database.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.Id == token.UserId && u.TenantId == token.TenantId && !u.IsDeleted,
                cancellationToken);

        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(token.FamilyId, now, "user is no longer active", cancellationToken);
            return Error.Unauthorized("auth.user.inactive", "This account is no longer active.");
        }

        token.Consume(now);

        // Same family: the chain is what makes reuse detectable.
        return await IssueAsync(user, clientIp, userAgent, token.FamilyId, cancellationToken);
    }

    /// <summary>Revokes one refresh token — a single-device sign-out.</summary>
    public async Task<Result> RevokeAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var hash = RefreshToken.Hash(rawRefreshToken);
        var token = await database.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null)
        {
            // Do not reveal whether the token existed; sign-out is idempotent either way.
            return Result.Success();
        }

        token.Revoke(clock.UtcNow, "signed out");
        await database.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Revokes every refresh token in a family.</summary>
    public async Task RevokeFamilyAsync(
        Guid familyId,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken)
    {
        var family = await database.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.Revoke(now, reason);
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Signs the user out of every device: revokes all tokens and rotates the stamp.</summary>
    public async Task RevokeAllSessionsAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;

        var tokens = await database.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.Revoke(now, "signed out of all sessions");
        }

        user.RevokeAllSessions();
        await database.SaveChangesAsync(cancellationToken);
        await PublishSecurityStampAsync(user, cancellationToken);
    }

    /// <summary>
    /// Mirrors the user's current security stamp into Redis, where every resource service
    /// checks it. Without this, rotating the stamp would only take effect as tokens expired.
    /// </summary>
    public async Task PublishSecurityStampAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Only needs to outlive the longest access token that could still be in flight.
        var retention = TimeSpan.FromMinutes(_options.AccessTokenMinutes * 4);

        await revocations.SetSecurityStampAsync(user.Id, user.SecurityStamp, retention, cancellationToken);
    }

    public string CreateAccessToken(User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);

        var roles = user.Roles.Select(r => r.Role).ToArray();
        var permissions = SamaRoles.PermissionsFor(roles).ToArray();

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [SamaClaimTypes.Subject] = user.Id.ToString(),
            [SamaClaimTypes.TenantId] = user.TenantId.ToString(),
            [SamaClaimTypes.Email] = user.Email,
            [SamaClaimTypes.DisplayName] = user.DisplayName,
            [SamaClaimTypes.TokenId] = Guid.CreateVersion7().ToString(),
            [SamaClaimTypes.TokenType] = SamaClaimTypes.AccessTokenType,
            [SamaClaimTypes.SecurityStamp] = user.SecurityStamp,

            // Arrays serialise as JSON arrays and arrive as repeated claims on the principal.
            [SamaClaimTypes.Role] = roles,
            [SamaClaimTypes.Permission] = permissions,
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(_options.AccessTokenMinutes).UtcDateTime,
            Claims = claims,
            SigningCredentials = keys.ActiveCredentials,
        };

        return TokenHandler.CreateToken(descriptor);
    }

    /// <summary>Claims for the caller's own profile endpoint.</summary>
    public static UserProfileResponse ToProfile(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var roles = user.Roles.Select(r => r.Role).ToArray();

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.DisplayName,
            user.EmailConfirmed,
            roles,
            [.. SamaRoles.PermissionsFor(roles)]);
    }

    /// <summary>Reads the jti and expiry off the caller's own bearer token, for logout denylisting.</summary>
    public static (Guid? TokenId, DateTimeOffset? ExpiresAt) ReadTokenMetadata(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        Guid? tokenId = Guid.TryParse(principal.FindFirst(SamaClaimTypes.TokenId)?.Value, out var id) ? id : null;

        DateTimeOffset? expiresAt = long.TryParse(principal.FindFirst("exp")?.Value, out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp)
            : null;

        return (tokenId, expiresAt);
    }
}
