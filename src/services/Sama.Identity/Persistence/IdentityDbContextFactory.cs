using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;

namespace Sama.Identity.Persistence;

/// <summary>
/// Design-time context for <c>dotnet ef</c>.
/// <para>
/// Without this, the tooling boots the real host to find a context — which would demand
/// Redis, Pub/Sub and a full signing-key configuration just to scaffold a migration. This
/// keeps schema work to a single dependency: a reachable database.
/// </para>
/// </summary>
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SAMA_DESIGNTIME_POSTGRES")
            ?? "Host=localhost;Port=55432;Database=sama;Username=sama_identity;Password=sama_dev_password";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
            .Options;

        return new IdentityDbContext(options, SystemCurrentUser.Instance, FixedTenantContext.None, new SystemClock());
    }
}
