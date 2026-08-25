using Sama.Shared.Kernel.Domain;
using Sama.Shared.Messaging.Contracts;

namespace Sama.Identity.Domain;

/// <summary>
/// A person who can sign in. Credentials, lockout state and role grants all live here; the
/// aggregate is the only place any of them may change, which keeps the security-stamp
/// bookkeeping (the thing that invalidates issued tokens) impossible to forget.
/// </summary>
public sealed class User : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    private readonly List<UserRole> _roles = [];

    private User() { }

    private User(Guid id, Guid tenantId, string email, string displayName, string passwordHash)
        : base(id)
    {
        TenantId = tenantId;
        Email = email;
        NormalizedEmail = Normalize(email);
        DisplayName = displayName;
        PasswordHash = passwordHash;
        SecurityStamp = NewSecurityStamp();
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Upper-cased email, uniquely indexed. Comparing on a normalised column rather than with
    /// <c>lower(email)</c> at query time keeps the lookup index-backed, and it makes
    /// "Ahmed@x.com" and "ahmed@x.com" one account instead of two.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    public bool EmailConfirmed { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Changes whenever credentials or grants change. It is embedded in every access token, so
    /// rotating it invalidates all of them at once without waiting for expiry.
    /// </summary>
    public string SecurityStamp { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    /// <summary>Set while the account is locked out after repeated failed sign-ins.</summary>
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    // IAuditable
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    // ISoftDeletable
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    public static User Register(Guid tenantId, string email, string displayName, string passwordHash)
    {
        var user = new User(SamaId.New(), tenantId, email, displayName, passwordHash);
        user._roles.Add(new UserRole(user.Id, Shared.Security.Permissions.SamaRoles.Customer));
        user.Raise(new UserRegistered(user.Id, tenantId, user.Email));
        return user;
    }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } until && until > now;

    /// <summary>
    /// Records a failed sign-in and locks the account once the threshold is reached.
    /// A lockout window is the cheap defence against online password guessing that survives
    /// an attacker rotating IPs to dodge the rate limiter.
    /// </summary>
    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;

        if (AccessFailedCount >= maxAttempts)
        {
            LockoutEndsAt = now.Add(lockoutDuration);
            AccessFailedCount = 0;
        }
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void SetPasswordHash(string passwordHash, bool invalidateSessions = true)
    {
        PasswordHash = passwordHash;

        if (invalidateSessions)
        {
            // A password change must not leave previously-issued tokens usable — that is the
            // whole point of changing it after a suspected compromise.
            SecurityStamp = NewSecurityStamp();
        }
    }

    /// <summary>Rotates the stamp, signing the user out of every device.</summary>
    public void RevokeAllSessions() => SecurityStamp = NewSecurityStamp();

    public void ConfirmEmail() => EmailConfirmed = true;

    public void Deactivate()
    {
        IsActive = false;
        SecurityStamp = NewSecurityStamp();
    }

    public void Activate() => IsActive = true;

    public void AssignRole(string role)
    {
        if (_roles.Any(r => string.Equals(r.Role, role, StringComparison.Ordinal)))
        {
            return;
        }

        _roles.Add(new UserRole(Id, role));

        // Permissions are baked into access tokens, so a grant only takes effect once the
        // holder's token is reissued. Rotating the stamp forces that immediately.
        SecurityStamp = NewSecurityStamp();
    }

    public void RemoveRole(string role)
    {
        var existing = _roles.FirstOrDefault(r => string.Equals(r.Role, role, StringComparison.Ordinal));
        if (existing is null)
        {
            return;
        }

        _roles.Remove(existing);
        SecurityStamp = NewSecurityStamp();
    }

    public void SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    public void SetUpdated(DateTimeOffset at, Guid? by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }

    public void MarkDeleted(DateTimeOffset at, Guid? by)
    {
        IsDeleted = true;
        DeletedAt = at;
        DeletedBy = by;
        IsActive = false;
        SecurityStamp = NewSecurityStamp();
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}

/// <summary>A role grant. Roles resolve to permissions at token-issue time.</summary>
public sealed class UserRole
{
    private UserRole() { }

    public UserRole(Guid userId, string role)
    {
        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; private set; }

    public string Role { get; private set; } = string.Empty;
}
