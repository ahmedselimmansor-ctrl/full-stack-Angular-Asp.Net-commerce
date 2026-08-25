using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Inventory.Persistence;

internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SAMA_DESIGNTIME_POSTGRES")
            ?? "Host=localhost;Port=55432;Database=sama;Username=sama_inventory;Password=sama_dev_password";

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.SchemaName))
            .Options;

        return new InventoryDbContext(options, SystemCurrentUser.Instance, FixedTenantContext.None, new SystemClock());
    }
}
