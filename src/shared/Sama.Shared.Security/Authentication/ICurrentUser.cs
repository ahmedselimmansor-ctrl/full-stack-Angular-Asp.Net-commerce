using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Sama.Shared.Security.Authentication;

/// <summary>
/// The caller behind the current request. Injected rather than read from
/// <see cref="IHttpContextAccessor"/> at the call site, so domain and data code can be
/// tested with a stub instead of a fake HTTP context.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The user id, or <c>null</c> for anonymous callers (guest browsing, guest basket).</summary>
    Guid? UserId { get; }

    /// <summary>Tenant the request belongs to. Never null on an authenticated request.</summary>
    Guid? TenantId { get; }

    string? Email { get; }

    IReadOnlySet<string> Roles { get; }

    IReadOnlySet<string> Permissions { get; }

    bool HasPermission(string permission);

    bool IsInRole(string role);

    /// <summary>The user id, or a throw. For code paths that are unreachable while anonymous.</summary>
    Guid RequireUserId();

    Guid RequireTenantId();
}

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
        => Guid.TryParse(Principal?.FindFirstValue(SamaClaimTypes.Subject), out var id) ? id : null;

    public Guid? TenantId
        => Guid.TryParse(Principal?.FindFirstValue(SamaClaimTypes.TenantId), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(SamaClaimTypes.Email);

    public IReadOnlySet<string> Roles => _roles ??= Collect(SamaClaimTypes.Role);

    public IReadOnlySet<string> Permissions => _permissions ??= Collect(SamaClaimTypes.Permission);

    private IReadOnlySet<string>? _roles;
    private IReadOnlySet<string>? _permissions;

    private IReadOnlySet<string> Collect(string claimType)
    {
        var principal = Principal;
        if (principal is null)
        {
            return System.Collections.Frozen.FrozenSet<string>.Empty;
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var claim in principal.FindAll(claimType))
        {
            values.Add(claim.Value);
        }

        return values;
    }

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public bool IsInRole(string role) => Roles.Contains(role);

    public Guid RequireUserId()
        => UserId ?? throw new InvalidOperationException("The current request has no authenticated user.");

    public Guid RequireTenantId()
        => TenantId ?? throw new InvalidOperationException("The current request has no tenant context.");
}
