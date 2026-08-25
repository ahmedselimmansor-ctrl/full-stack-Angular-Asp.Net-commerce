using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Sama.Shared.Infrastructure.Persistence;

public static class DbContextRegistration
{
    /// <summary>
    /// Registers a service's DbContext against Cloud SQL with the settings that matter in a
    /// clustered deployment: a shared data source, connection resiliency, and a command
    /// timeout short enough that a stuck query fails the request instead of the pod.
    /// </summary>
    /// <param name="schema">
    /// The service's PostgreSQL schema. Passed explicitly because the migrations history table
    /// must be schema-qualified: left unqualified it resolves through <c>search_path</c> to
    /// <c>public</c>, which no service role may write to.
    /// </param>
    public static IServiceCollection AddSamaDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string schema,
        string connectionStringName = "Postgres")
        where TContext : SamaDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is not configured.");

        // One data source per context type: it owns the connection pool, and building one per
        // request would defeat pooling entirely.
        services.AddKeyedSingleton(typeof(TContext).Name, (_, _) =>
        {
            // Npgsql emits its own spans and metrics; the tracer/meter subscribe to them via
            // AddNpgsql()/AddNpgsqlInstrumentation() in TelemetryRegistration, so a slow
            // endpoint can be attributed down to the individual statement.
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            return dataSourceBuilder.Build();
        });

        services.AddDbContext<TContext>((provider, options) =>
        {
            var dataSource = provider.GetRequiredKeyedService<NpgsqlDataSource>(typeof(TContext).Name);

            options.UseNpgsql(dataSource, npgsql =>
            {
                // Cloud SQL drops connections during maintenance failovers; without retries
                // that surfaces as a burst of 500s rather than a brief latency bump.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);

                npgsql.CommandTimeout(30);

                // Migrations are applied by a Kubernetes Job, and the history table lives
                // alongside the service's own tables rather than in public.
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
            });

            if (environment.IsDevelopment())
            {
                // Parameter values in logs are a privacy incident in production and a
                // debugging necessity locally.
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // Pooling is deliberately not used: SamaDbContext takes the scoped ICurrentUser so the
        // tenant filter can see the caller, and a pooled context cannot accept scoped
        // dependencies. Correct tenant isolation is worth more than the allocation saving.
        return services;
    }
}
