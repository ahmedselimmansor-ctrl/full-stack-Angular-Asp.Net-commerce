using System.ComponentModel.DataAnnotations;

namespace Sama.Shared.Infrastructure.RateLimiting;

/// <summary>A single named bucket: <see cref="PermitLimit"/> requests per <see cref="WindowSeconds"/>.</summary>
public sealed class RateLimitPolicy
{
    [Range(1, 1_000_000)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// What the bucket is keyed by. <c>user</c> falls back to the client IP for anonymous
    /// callers, so an unauthenticated flood is still bounded.
    /// </summary>
    public RateLimitPartition Partition { get; set; } = RateLimitPartition.User;
}

public enum RateLimitPartition
{
    /// <summary>Per authenticated user, falling back to client IP when anonymous.</summary>
    User = 0,

    /// <summary>Per client IP, regardless of authentication.</summary>
    IpAddress = 1,

    /// <summary>Per API key, for partner/server-to-server traffic.</summary>
    ApiKey = 2,

    /// <summary>
    /// Per IP <i>and</i> the submitted identifier (username/email). Credential stuffing rotates
    /// the username on one IP and the IP on one username; this catches both halves.
    /// </summary>
    IpAndSubject = 3,
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When Redis is unreachable: <c>true</c> lets traffic through, <c>false</c> rejects it.
    /// Failing open keeps the shop trading during a cache outage and is the right default for
    /// a storefront; the auth endpoints override it to fail closed.
    /// </summary>
    public bool FailOpen { get; set; } = true;

    /// <summary>Named policies. Endpoints reference these by name.</summary>
    public Dictionary<string, RateLimitPolicy> Policies { get; init; } = new(StringComparer.Ordinal);
}
