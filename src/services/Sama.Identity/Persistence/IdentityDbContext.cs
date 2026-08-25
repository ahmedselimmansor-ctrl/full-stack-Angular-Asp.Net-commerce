using Microsoft.EntityFrameworkCore;
using Sama.Identity.Domain;
using Sama.Shared.Infrastructure.Persistence;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Identity.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IClock clock) : SamaDbContext(options, currentUser, tenantContext, clock)
{
    /// <summary>This service's PostgreSQL schema, and the only one its DB role may write to.</summary>
    public const string SchemaName = "identity";

    protected override string Schema => SchemaName;

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureDomain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Email).HasMaxLength(320).IsRequired();
            entity.Property(u => u.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();

            // Unique per tenant, not globally: the same person may hold an account on two
            // storefronts. Filtered on is_deleted so a soft-deleted account does not
            // permanently reserve its address.
            entity.HasIndex(u => new { u.TenantId, u.NormalizedEmail })
                .IsUnique()
                .HasFilter("is_deleted = false")
                .HasDatabaseName("ux_users_tenant_email");

            entity.OwnsMany(u => u.Roles, roles =>
            {
                roles.ToTable("user_roles");
                roles.WithOwner().HasForeignKey(r => r.UserId);
                roles.Property(r => r.Role).HasMaxLength(64).IsRequired();
                roles.HasKey(r => new { r.UserId, r.Role });
            });

            // Roles are read on every token issue, which happens on every login and refresh.
            entity.Navigation(u => u.Roles).AutoInclude();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.RevokedReason).HasMaxLength(200);
            entity.Property(t => t.CreatedByIp).HasMaxLength(64);
            entity.Property(t => t.UserAgent).HasMaxLength(400);

            // The refresh path looks a token up by hash on every call; this is its index.
            entity.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");

            // Reuse detection revokes a whole family at once.
            entity.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family");

            entity.HasIndex(t => new { t.UserId, t.ExpiresAt }).HasDatabaseName("ix_refresh_tokens_user");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
