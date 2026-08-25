using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Ordering.Persistence;

internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SAMA_DESIGNTIME_POSTGRES")
            ?? "Host=localhost;Port=55432;Database=sama;Username=sama_ordering;Password=sama_dev_password";

        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", OrderingDbContext.SchemaName))
            .Options;

        return new OrderingDbContext(options, SystemCurrentUser.Instance, FixedTenantContext.None, new SystemClock());
    }
}
