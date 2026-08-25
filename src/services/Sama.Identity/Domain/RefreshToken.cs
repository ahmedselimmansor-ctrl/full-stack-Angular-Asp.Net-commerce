using System.Security.Cryptography;
using System.Text;
using Sama.Shared.Kernel.Domain;

namespace Sama.Identity.Domain;

/// <summary>
/// A long-lived credential exchanged for short-lived access tokens.
/// <para>
/// Refresh tokens rotate: each use consumes the token and issues a replacement in the same
/// <see cref="FamilyId"/>. If a <i>consumed</i> token is ever presented again, the only
/// explanations are a stolen token or a cloned client, and the whole family is revoked. That
/// turns silent, indefinite account access into one failed request and a forced re-login.
/// </para>
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken() { }

    private RefreshToken(
        Guid id,
        Guid userId,
        Guid tenantId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string? createdByIp,
        string? userAgent)
        : base(id)
    {
        UserId = userId;
        TenantId = tenantId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
        UserAgent = userAgent;
    }

    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>Groups a rotation chain, so revoking a compromised token revokes its lineage.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>
    /// SHA-256 of the token. The raw value is returned to the client exactly once and never
    /// stored — a database leak then yields hashes, not usable credentials.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When this token was exchanged. Non-null means presenting it again is a replay.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public string? CreatedByIp { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsActive(DateTimeOffset now)
        => RevokedAt is null && ConsumedAt is null && ExpiresAt > now;

    public static RefreshToken Issue(
        Guid userId,
        Guid tenantId,
        Guid familyId,
        string rawToken,
        DateTimeOffset issuedAt,
        TimeSpan lifetime,
        string? createdByIp,
        string? userAgent)
        => new(
            SamaId.New(),
            userId,
            tenantId,
            familyId,
            Hash(rawToken),
            issuedAt,
            issuedAt.Add(lifetime),
            createdByIp,
            Truncate(userAgent, 400));

    public void Consume(DateTimeOffset now) => ConsumedAt = now;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    /// <summary>
    /// Generates a 256-bit token. Cryptographically random rather than a GUID: GUIDv4 carries
    /// only 122 bits and its generator is not a documented CSPRNG on every platform.
    /// </summary>
    public static string GenerateRawToken()
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string rawToken)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Truncate(string? value, int max)
        => value is null ? null : value.Length <= max ? value : value[..max];
}
