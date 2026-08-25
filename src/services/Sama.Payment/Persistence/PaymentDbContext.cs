using Microsoft.EntityFrameworkCore;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Payment.Persistence;

public sealed class PaymentDbContext(
    DbContextOptions<PaymentDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IClock clock) : SamaDbContext(options, currentUser, tenantContext, clock)
{
    public const string SchemaName = "payment";

    protected override string Schema => SchemaName;

    public DbSet<Domain.Payment> Payments => Set<Domain.Payment>();

    protected override void ConfigureDomain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            entity.Property(p => p.Amount).HasColumnType("numeric(18,2)");
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.ProviderReference).HasMaxLength(200);
            entity.Property(p => p.FailureReason).HasMaxLength(500);

            // The database guarantee behind "charge an order at most once": a redelivered
            // payment.requested cannot create a second charge, because this insert fails.
            entity.HasIndex(p => p.OrderId)
                .IsUnique()
                .HasDatabaseName("ux_payments_order");

            entity.HasIndex(p => new { p.TenantId, p.OwnerId, p.CreatedAt })
                .HasDatabaseName("ix_payments_customer");

            entity.HasIndex(p => p.ProviderReference)
                .HasDatabaseName("ix_payments_provider_reference");
        });
    }
}
