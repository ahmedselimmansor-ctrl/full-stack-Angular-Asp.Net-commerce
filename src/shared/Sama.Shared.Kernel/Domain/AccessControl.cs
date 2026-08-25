namespace Sama.Shared.Kernel.Domain;

/// <summary>
/// Marks an entity as belonging to a tenant (a storefront/brand). The EF Core interceptor
/// in Sama.Shared.Infrastructure applies a global query filter on this, so a missing
/// <c>WHERE tenant_id = ...</c> in a hand-written query cannot leak across tenants.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}

/// <summary>
/// Marks an entity that belongs to one end user. Combined with the caller's identity this
/// yields row-level access control — a customer reads their own orders and nobody else's.
/// </summary>
public interface IUserOwned
{
    Guid OwnerId { get; }
}

/// <summary>Rows are retained and filtered out rather than physically deleted.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }

    void MarkDeleted(DateTimeOffset at, Guid? by);
}

/// <summary>Audit columns stamped automatically by the SaveChanges interceptor.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? UpdatedAt { get; }

    Guid? UpdatedBy { get; }

    void SetCreated(DateTimeOffset at, Guid? by);

    void SetUpdated(DateTimeOffset at, Guid? by);
}
