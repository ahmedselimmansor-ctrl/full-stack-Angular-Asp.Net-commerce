namespace Sama.Shared.Security.Permissions;

/// <summary>
/// Roles are named bundles of permissions, resolved at token-issue time. Changing a
/// bundle changes what existing holders can do as soon as their access token refreshes
/// (default 15 minutes), without a deploy.
/// </summary>
public static class SamaRoles
{
    public const string Customer = "customer";
    public const string Support = "support";
    public const string CatalogManager = "catalog_manager";
    public const string OrderManager = "order_manager";
    public const string Administrator = "administrator";

    private static readonly Dictionary<string, string[]> Map = new(StringComparer.Ordinal)
    {
        [Customer] =
        [
            SamaPermissions.Catalog.ReadProduct,
            SamaPermissions.Basket.ReadOwn,
            SamaPermissions.Basket.WriteOwn,
            SamaPermissions.Ordering.Create,
            SamaPermissions.Ordering.ReadOwn,
            SamaPermissions.Ordering.CancelOwn,
            SamaPermissions.Payment.ReadOwn,
        ],

        // Support can look, but cannot mutate money or catalogue.
        [Support] =
        [
            SamaPermissions.Catalog.ReadProduct,
            SamaPermissions.Ordering.ReadAny,
            SamaPermissions.Payment.ReadAny,
            SamaPermissions.Identity.ReadUser,
            SamaPermissions.Inventory.ReadStock,
        ],

        [CatalogManager] =
        [
            SamaPermissions.Catalog.ReadProduct,
            SamaPermissions.Catalog.WriteProduct,
            SamaPermissions.Catalog.DeleteProduct,
            SamaPermissions.Catalog.WriteCategory,
            SamaPermissions.Inventory.ReadStock,
            SamaPermissions.Inventory.WriteStock,
        ],

        [OrderManager] =
        [
            SamaPermissions.Catalog.ReadProduct,
            SamaPermissions.Ordering.ReadAny,
            SamaPermissions.Ordering.CancelAny,
            SamaPermissions.Ordering.Refund,
            SamaPermissions.Payment.ReadAny,
            SamaPermissions.Payment.Refund,
            SamaPermissions.Inventory.ReadStock,
        ],

        // Enumerated rather than "all", so adding a permission is a deliberate grant.
        [Administrator] = [.. SamaPermissions.All],
    };

    public static IReadOnlyCollection<string> AllRoles => Map.Keys;

    public static bool IsKnownRole(string role) => Map.ContainsKey(role);

    public static IReadOnlyCollection<string> PermissionsFor(string role)
        => Map.TryGetValue(role, out var permissions) ? permissions : [];

    /// <summary>Union of the permissions granted by every supplied role.</summary>
    public static IReadOnlySet<string> PermissionsFor(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            result.UnionWith(PermissionsFor(role));
        }

        return result;
    }
}
