namespace Sama.Shared.Security.Permissions;

/// <summary>
/// The permission catalogue. Authorization in Sama is permission-based, never role-based:
/// endpoints ask for <c>catalog:product:write</c>, not "is this user an Admin". Roles are
/// only a bundling convenience, so re-shaping who can do what never means editing endpoints.
/// <para>
/// Naming is <c>area:resource:action</c>. An action suffixed <c>.own</c> is row-scoped —
/// the holder may act on their own rows only; <c>.any</c> is the unrestricted counterpart.
/// <see cref="AccessControl.AccessScope"/> turns that pair into a query filter.
/// </para>
/// </summary>
public static class SamaPermissions
{
    public static class Catalog
    {
        public const string ReadProduct = "catalog:product:read";
        public const string WriteProduct = "catalog:product:write";
        public const string DeleteProduct = "catalog:product:delete";
        public const string WriteCategory = "catalog:category:write";
    }

    public static class Basket
    {
        public const string ReadOwn = "basket:basket:read.own";
        public const string WriteOwn = "basket:basket:write.own";
    }

    public static class Ordering
    {
        public const string Create = "ordering:order:create";
        public const string ReadOwn = "ordering:order:read.own";
        public const string ReadAny = "ordering:order:read.any";
        public const string CancelOwn = "ordering:order:cancel.own";
        public const string CancelAny = "ordering:order:cancel.any";
        public const string Refund = "ordering:order:refund";
    }

    public static class Inventory
    {
        public const string ReadStock = "inventory:stock:read";
        public const string WriteStock = "inventory:stock:write";
    }

    public static class Payment
    {
        public const string ReadOwn = "payment:payment:read.own";
        public const string ReadAny = "payment:payment:read.any";
        public const string Refund = "payment:payment:refund";
    }

    public static class Identity
    {
        public const string ReadUser = "identity:user:read";
        public const string WriteUser = "identity:user:write";
        public const string AssignRole = "identity:role:assign";
    }

    /// <summary>Every permission the platform knows about. Used to validate role definitions at startup.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Catalog.ReadProduct, Catalog.WriteProduct, Catalog.DeleteProduct, Catalog.WriteCategory,
        Basket.ReadOwn, Basket.WriteOwn,
        Ordering.Create, Ordering.ReadOwn, Ordering.ReadAny, Ordering.CancelOwn, Ordering.CancelAny, Ordering.Refund,
        Inventory.ReadStock, Inventory.WriteStock,
        Payment.ReadOwn, Payment.ReadAny, Payment.Refund,
        Identity.ReadUser, Identity.WriteUser, Identity.AssignRole,
    };
}
