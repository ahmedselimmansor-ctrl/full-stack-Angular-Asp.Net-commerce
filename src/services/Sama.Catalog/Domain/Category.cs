using Sama.Shared.Kernel.Domain;

namespace Sama.Catalog.Domain;

/// <summary>
/// A node in the storefront's category tree. Self-referencing rather than a fixed depth, so
/// merchandising can reorganise without a schema change.
/// </summary>
public sealed class Category : AggregateRoot<Guid>, ITenantScoped, IAuditable, ISoftDeletable
{
    private Category() { }

    private Category(Guid id, Guid tenantId, string name, string slug, Guid? parentId)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Slug = slug;
        ParentId = parentId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL segment. Unique per tenant, so category URLs stay stable and cacheable.</summary>
    public string Slug { get; private set; } = string.Empty;

    public Guid? ParentId { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    public static Category Create(Guid tenantId, string name, string slug, Guid? parentId, int sortOrder = 0)
        => new(SamaId.New(), tenantId, name, slug, parentId) { SortOrder = sortOrder };

    public void Rename(string name, string slug)
    {
        Name = name;
        Slug = slug;
    }

    public void Reparent(Guid? parentId)
    {
        if (parentId == Id)
        {
            throw new InvalidOperationException("A category cannot be its own parent.");
        }

        ParentId = parentId;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void SetCreated(DateTimeOffset at, Guid? by) { CreatedAt = at; CreatedBy = by; }
    public void SetUpdated(DateTimeOffset at, Guid? by) { UpdatedAt = at; UpdatedBy = by; }

    public void MarkDeleted(DateTimeOffset at, Guid? by)
    {
        IsDeleted = true;
        DeletedAt = at;
        DeletedBy = by;
        IsActive = false;
    }
}
