using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Catalog.Persistence;

internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SAMA_DESIGNTIME_POSTGRES")
            ?? "Host=localhost;Port=55432;Database=sama;Username=sama_catalog;Password=sama_dev_password";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName))
            .Options;

        return new CatalogDbContext(options, SystemCurrentUser.Instance, FixedTenantContext.None, new SystemClock());
    }
}
