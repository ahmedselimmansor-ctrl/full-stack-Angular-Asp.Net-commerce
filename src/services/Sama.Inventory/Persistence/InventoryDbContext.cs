using Microsoft.EntityFrameworkCore;
using Sama.Inventory.Domain;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Inventory.Persistence;

public sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IClock clock) : SamaDbContext(options, currentUser, tenantContext, clock)
{
    public const string SchemaName = "inventory";

    protected override string Schema => SchemaName;

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    protected override void ConfigureDomain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.ToTable("stock_items");
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Sku).HasMaxLength(64).IsRequired();

            entity.HasIndex(s => new { s.TenantId, s.ProductId })
                .IsUnique()
                .HasDatabaseName("ux_stock_items_product");

            // Supports the low-stock report without scanning the whole table.
            entity.HasIndex(s => new { s.TenantId, s.QuantityOnHand })
                .HasDatabaseName("ix_stock_items_levels");
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("stock_reservations");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

            // The database-level guarantee behind idempotent consumption: a redelivered
            // order.placed cannot create a second hold, because the insert violates this.
            entity.HasIndex(r => r.OrderId)
                .IsUnique()
                .HasDatabaseName("ux_stock_reservations_order");

            entity.OwnsMany(r => r.Lines, lines =>
            {
                lines.ToTable("stock_reservation_lines");
                lines.WithOwner().HasForeignKey(l => l.ReservationId);
                lines.HasKey(l => new { l.ReservationId, l.ProductId });
            });

            entity.Navigation(r => r.Lines).AutoInclude();
        });
    }
}
