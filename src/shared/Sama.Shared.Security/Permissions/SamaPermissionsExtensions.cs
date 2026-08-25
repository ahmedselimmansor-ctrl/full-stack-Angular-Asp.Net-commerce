using Microsoft.AspNetCore.Builder;
using Sama.Shared.Security.Authorization;

namespace Sama.Shared.Security.Permissions;

/// <summary>Fluent permission guards for minimal-API routes.</summary>
public static class SamaPermissionsExtensions
{
    /// <summary>
    /// Requires a single permission: <c>app.MapPost("/products", …).RequirePermission(Catalog.WriteProduct)</c>.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
    }

    /// <summary>
    /// Requires <i>any one</i> of the supplied permissions — the shape a resource guarded by an
    /// <c>.own</c>/<c>.any</c> pair needs, where the row filter does the rest of the work.
    /// </summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfZero(permissions.Length);

        // One synthesised policy per combination; the provider caches by name.
        return builder.RequireAuthorization(
            PermissionPolicyProvider.AnyPrefix + string.Join('|', permissions));
    }
}
