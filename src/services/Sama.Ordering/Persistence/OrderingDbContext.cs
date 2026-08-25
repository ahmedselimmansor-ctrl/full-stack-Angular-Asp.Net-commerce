using Microsoft.EntityFrameworkCore;
using Sama.Ordering.Domain;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Ordering.Persistence;

public sealed class OrderingDbContext(
    DbContextOptions<OrderingDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IClock clock) : SamaDbContext(options, currentUser, tenantContext, clock)
{
    public const string SchemaName = "ordering";

    protected override string Schema => SchemaName;

    public DbSet<Order> Orders => Set<Order>();

    protected override void ConfigureDomain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(o => o.Id);

            entity.Property(o => o.OrderNumber).HasMaxLength(32).IsRequired();
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(o => o.Currency).HasMaxLength(3).IsRequired();
            entity.Property(o => o.Total).HasColumnType("numeric(18,2)");
            entity.Property(o => o.CancellationReason).HasMaxLength(300);

            entity.Property(o => o.ShippingLine1).HasMaxLength(300).IsRequired();
            entity.Property(o => o.ShippingCity).HasMaxLength(150).IsRequired();
            entity.Property(o => o.ShippingPostcode).HasMaxLength(32).IsRequired();
            entity.Property(o => o.ShippingCountry).HasMaxLength(2).IsRequired();

            entity.HasIndex(o => new { o.TenantId, o.OrderNumber })
                .IsUnique()
                .HasDatabaseName("ux_orders_number");

            // "My orders, newest first" — the customer's own order history, which is by far
            // the most-run query on this table.
            entity.HasIndex(o => new { o.TenantId, o.OwnerId, o.CreatedAt })
                .HasDatabaseName("ix_orders_customer");

            // Back-office queues filter by state.
            entity.HasIndex(o => new { o.TenantId, o.Status, o.CreatedAt })
                .HasDatabaseName("ix_orders_status");

            entity.OwnsMany(o => o.Lines, lines =>
            {
                lines.ToTable("order_lines");
                lines.WithOwner().HasForeignKey(l => l.OrderId);
                lines.Property(l => l.Sku).HasMaxLength(64).IsRequired();
                lines.Property(l => l.Name).HasMaxLength(300).IsRequired();
                lines.Property(l => l.Currency).HasMaxLength(3).IsRequired();
                lines.Property(l => l.UnitPrice).HasColumnType("numeric(18,2)");
                lines.HasKey(l => new { l.OrderId, l.ProductId });
            });

            entity.Navigation(o => o.Lines).AutoInclude();
        });
    }
}
