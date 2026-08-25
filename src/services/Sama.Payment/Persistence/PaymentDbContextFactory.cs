using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Payment.Persistence;

internal sealed class PaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SAMA_DESIGNTIME_POSTGRES")
            ?? "Host=localhost;Port=55432;Database=sama;Username=sama_payment;Password=sama_dev_password";

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentDbContext.SchemaName))
            .Options;

        return new PaymentDbContext(options, SystemCurrentUser.Instance, FixedTenantContext.None, new SystemClock());
    }
}
