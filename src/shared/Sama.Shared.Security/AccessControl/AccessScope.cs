using System.Linq.Expressions;
using Sama.Shared.Kernel.Domain;
using Sama.Shared.Security.Authentication;

namespace Sama.Shared.Security.AccessControl;

/// <summary>How much of a collection the caller is allowed to see.</summary>
public enum AccessScope
{
    /// <summary>No access at all — the caller holds neither the <c>.own</c> nor the <c>.any</c> permission.</summary>
    None = 0,

    /// <summary>Rows the caller owns.</summary>
    Own = 1,

    /// <summary>Every row in the tenant.</summary>
    All = 2,
}

/// <summary>
/// Turns a permission pair into a row filter. This is the second half of data access
/// control: <see cref="SamaPermissionsExtensions"/> decides <i>whether</i> a caller may
/// read orders, this decides <i>which</i> orders — and it is applied inside the query, so
/// the database never returns rows the caller cannot see.
/// </summary>
public static class AccessScopeExtensions
{
    /// <summary>
    /// Resolves the scope from the two permissions that guard a resource. <c>.any</c> wins
    /// over <c>.own</c>, so an administrator is not accidentally narrowed to their own rows.
    /// </summary>
    public static AccessScope ScopeFor(this ICurrentUser user, string ownPermission, string anyPermission)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.HasPermission(anyPermission))
        {
            return AccessScope.All;
        }

        return user.HasPermission(ownPermission) ? AccessScope.Own : AccessScope.None;
    }

    /// <summary>
    /// Narrows a query to the caller's scope. <see cref="AccessScope.None"/> yields an
    /// always-false predicate rather than throwing: a listing endpoint returns an empty
    /// page, which does not reveal whether matching rows exist.
    /// </summary>
    public static IQueryable<TEntity> WithinScope<TEntity>(
        this IQueryable<TEntity> query,
        AccessScope scope,
        Guid? userId)
        where TEntity : IUserOwned
    {
        ArgumentNullException.ThrowIfNull(query);

        return scope switch
        {
            AccessScope.All => query,
            AccessScope.Own when userId is { } id => query.Where(OwnedBy<TEntity>(id)),
            _ => query.Where(_ => false),
        };
    }

    /// <summary>
    /// Authorises access to a single already-loaded resource. Use after a by-id lookup, where
    /// a query filter cannot help.
    /// </summary>
    public static bool CanAccess<TEntity>(this AccessScope scope, TEntity entity, Guid? userId)
        where TEntity : IUserOwned
    {
        ArgumentNullException.ThrowIfNull(entity);

        return scope switch
        {
            AccessScope.All => true,
            AccessScope.Own => userId is { } id && entity.OwnerId == id,
            _ => false,
        };
    }

    private static Expression<Func<TEntity, bool>> OwnedBy<TEntity>(Guid userId)
        where TEntity : IUserOwned
        => entity => entity.OwnerId == userId;
}
