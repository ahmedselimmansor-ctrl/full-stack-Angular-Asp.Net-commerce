using System.ComponentModel.DataAnnotations;

namespace Sama.Identity.Features.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Failed sign-ins before the account locks. Pairs with the per-IP rate limit: the limiter
    /// stops one address guessing many passwords, this stops many addresses guessing one.
    /// </summary>
    [Range(3, 20)]
    public int MaxFailedAccessAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Require a confirmed email address before the first sign-in succeeds.</summary>
    public bool RequireConfirmedEmail { get; set; }
}
