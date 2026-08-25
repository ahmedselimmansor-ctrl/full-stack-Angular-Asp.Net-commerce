using System.ComponentModel.DataAnnotations;

namespace Sama.Shared.Security.Authentication;

/// <summary>Token-validation settings. Every service that accepts a Sama token binds these.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Base URL of Sama.Identity. The bearer handler reads
    /// <c>{Authority}/.well-known/openid-configuration</c> for the signing keys and refreshes
    /// them on its own, so key rotation needs no redeploy of the consuming services.
    /// </summary>
    [Required]
    public string Authority { get; set; } = string.Empty;

    /// <summary>Expected <c>aud</c>. Stops a token minted for another audience being replayed here.</summary>
    [Required]
    public string Audience { get; set; } = "sama-api";

    /// <summary>Expected <c>iss</c>. Usually identical to <see cref="Authority"/>.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Off only for in-cluster plaintext traffic, where mTLS/network policy already bounds who
    /// can reach the metadata endpoint. Must stay on for anything crossing a trust boundary.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Allowed clock drift. The framework default is 5 minutes, which quietly extends every
    /// token's life; GKE nodes are NTP-synced, so 30 seconds is generous.
    /// </summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;
}
