using Microsoft.EntityFrameworkCore;
using Sama.Catalog.Domain;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Catalog.Persistence;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IClock clock) : SamaDbContext(options, currentUser, tenantContext, clock)
{
    public const string SchemaName = "catalog";

    protected override string Schema => SchemaName;

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void ConfigureDomain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name).HasMaxLength(200).IsRequired();
            entity.Property(c => c.Slug).HasMaxLength(200).IsRequired();

            entity.HasIndex(c => new { c.TenantId, c.Slug })
                .IsUnique()
                .HasFilter("is_deleted = false")
                .HasDatabaseName("ux_categories_tenant_slug");

            entity.HasIndex(c => new { c.TenantId, c.ParentId }).HasDatabaseName("ix_categories_parent");

            entity.HasOne<Category>()
                .WithMany()
                .HasForeignKey(c => c.ParentId)

                // Restrict, not Cascade: deleting a parent must not silently take its whole
                // subtree (and every product pointing into it) with it.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(300).IsRequired();
            entity.Property(p => p.Slug).HasMaxLength(300).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(8000).IsRequired();
            entity.Property(p => p.Brand).HasMaxLength(200);
            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();

            // numeric(18,2), not double. Binary floating point cannot represent 0.10 exactly,
            // and money that does not add up is the fastest way to lose a merchant's trust.
            entity.Property(p => p.Price).HasColumnType("numeric(18,2)");

            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(p => new { p.TenantId, p.Sku })
                .IsUnique()
                .HasFilter("is_deleted = false")
                .HasDatabaseName("ux_products_tenant_sku");

            entity.HasIndex(p => new { p.TenantId, p.Slug })
                .IsUnique()
                .HasFilter("is_deleted = false")
                .HasDatabaseName("ux_products_tenant_slug");

            // Covers the storefront's default listing: a category page ordered by price.
            entity.HasIndex(p => new { p.TenantId, p.CategoryId, p.Status, p.Price })
                .HasDatabaseName("ix_products_browse");

            // Database-generated tsvector plus a GIN index. Weighted so a term matching the
            // product name outranks the same term buried in a description.
            entity.HasGeneratedTsVectorColumn(
                    p => p.SearchVector,
                    "english",
                    p => new { p.Name, p.Brand, p.Description })
                .HasIndex(p => p.SearchVector)
                .HasMethod("GIN")
                .HasDatabaseName("ix_products_search");

            entity.OwnsMany(p => p.Images, images =>
            {
                images.ToTable("product_images");
                images.WithOwner().HasForeignKey(i => i.ProductId);
                images.Property(i => i.Url).HasMaxLength(1000).IsRequired();
                images.Property(i => i.AltText).HasMaxLength(300);
                images.HasKey(i => new { i.ProductId, i.Url });
            });

            entity.Navigation(p => p.Images).AutoInclude();

            entity.HasOne<Category>()
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
