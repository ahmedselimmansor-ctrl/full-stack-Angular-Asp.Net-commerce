namespace Sama.Shared.Security.Authentication;

/// <summary>
/// A non-request identity, for code that runs outside an HTTP call: migration tooling,
/// background workers and integration-test fixtures.
/// <para>
/// It reports no user and no tenant. That matters — a background context therefore has to
/// opt out of the tenant filter explicitly (<c>TenantFilterEnabled = false</c>) rather than
/// silently inheriting some arbitrary tenant, which would be a very quiet way to write rows
/// into the wrong storefront.
/// </para>
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public static SystemCurrentUser Instance { get; } = new();

    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? TenantId => null;

    public string? Email => null;

    public IReadOnlySet<string> Roles => System.Collections.Frozen.FrozenSet<string>.Empty;

    public IReadOnlySet<string> Permissions => System.Collections.Frozen.FrozenSet<string>.Empty;

    public bool HasPermission(string permission) => false;

    public bool IsInRole(string role) => false;

    public Guid RequireUserId()
        => throw new InvalidOperationException("This operation is running without a user context.");

    public Guid RequireTenantId()
        => throw new InvalidOperationException("This operation is running without a tenant context.");
}
