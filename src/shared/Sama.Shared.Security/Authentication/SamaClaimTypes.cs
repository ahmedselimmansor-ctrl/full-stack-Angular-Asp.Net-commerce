namespace Sama.Shared.Security.Authentication;

/// <summary>
/// Claim names used in Sama access tokens. Short names keep the token small — it rides on
/// every request from every client, including mobile on metered networks.
/// </summary>
public static class SamaClaimTypes
{
    /// <summary>Subject: the user id (UUIDv7).</summary>
    public const string Subject = "sub";

    /// <summary>Tenant id. Drives the tenant query filter on every scoped entity.</summary>
    public const string TenantId = "tid";

    /// <summary>A granted permission. Repeated once per permission.</summary>
    public const string Permission = "perm";

    /// <summary>A granted role. Repeated once per role.</summary>
    public const string Role = "role";

    /// <summary>Token id, used to revoke a single token.</summary>
    public const string TokenId = "jti";

    /// <summary>Token kind: <c>access</c> or <c>refresh</c>. Stops a refresh token being replayed as an access token.</summary>
    public const string TokenType = "typ";

    /// <summary>
    /// Security stamp. Bumped when the user changes password or an admin revokes access;
    /// tokens carrying a stale stamp are rejected without waiting for expiry.
    /// </summary>
    public const string SecurityStamp = "sst";

    public const string Email = "email";
    public const string DisplayName = "name";

    public const string AccessTokenType = "access";
    public const string RefreshTokenType = "refresh";
}
