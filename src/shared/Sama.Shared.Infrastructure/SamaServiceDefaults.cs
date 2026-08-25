using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Sama.Shared.Infrastructure.Caching;
using Sama.Shared.Infrastructure.Http;
using Sama.Shared.Infrastructure.Idempotency;
using Sama.Shared.Infrastructure.Observability;
using Sama.Shared.Infrastructure.RateLimiting;
using Sama.Shared.Infrastructure.Redis;
using Sama.Shared.Kernel;
using Sama.Shared.Kernel.Time;
using Sama.Shared.Security;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Tenancy;
using Scalar.AspNetCore;
using Serilog;

namespace Sama.Shared.Infrastructure;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "sama-clients";

    /// <summary>
    /// Exact origins allowed to call the API from a browser. Never a wildcard: the API
    /// answers with credentials, and <c>Access-Control-Allow-Origin: *</c> is invalid with
    /// credentials anyway — a wildcard here would either break or over-share.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}

public static class SamaServiceDefaults
{
    /// <summary>
    /// Registers everything a Sama service shares: logging, telemetry, Redis, caching, rate
    /// limiting, idempotency, auth, health, and problem-details error handling.
    /// </summary>
    /// <param name="useRemoteAuthentication">
    /// True for resource services, which validate tokens against Sama.Identity's published
    /// keys. False for Sama.Identity itself: it holds the signing keys locally, and pointing
    /// its bearer handler at its own HTTP metadata endpoint would make the service depend on
    /// itself being already up to authenticate the first request.
    /// </param>
    public static WebApplicationBuilder AddSamaServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        bool useRemoteAuthentication = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuration = builder.Configuration;

        builder.Host.UseSerilog((context, services, loggerConfiguration) =>
        {
            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName);

            var projectId = context.Configuration["GoogleCloud:ProjectId"] ?? string.Empty;

            if (context.HostingEnvironment.IsDevelopment())
            {
                loggerConfiguration.WriteTo.Console();
            }
            else
            {
                loggerConfiguration.WriteTo.Console(new GoogleCloudJsonFormatter(projectId));
            }
        });

        builder.Services.AddSingleton<IClock, SystemClock>();

        builder.Services.AddSamaTelemetry(configuration, serviceName);

        // --- Error handling -------------------------------------------------------------
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        // --- Redis-backed platform services ---------------------------------------------
        builder.Services.AddSamaRedis(configuration);

        var redisConnection = configuration.GetSection(RedisOptions.SectionName)["ConnectionString"];

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = configuration.GetSection(RedisOptions.SectionName)["InstanceName"] + ":hc:";
        });

        builder.Services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromSeconds(30),
            };

            // A cached payload larger than this is almost always an unbounded list that
            // should have been paged; capping it stops one query evicting the whole L1.
            options.MaximumPayloadBytes = 1024 * 1024;
        });
        builder.Services.AddSingleton<ISamaCache, HybridSamaCache>();

        builder.Services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IRateLimiter, RedisSlidingWindowRateLimiter>();

        builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

        builder.Services.Configure<ClientIpOptions>(configuration.GetSection(ClientIpOptions.SectionName));

        // --- Security -------------------------------------------------------------------
        // Registered unconditionally: the DbContext resolves ICurrentUser to apply the tenant
        // filter, so it must exist whichever authentication mode the service uses.
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();

        // Tenant resolution is needed on anonymous paths too (storefront browsing, login),
        // so it is registered independently of any authentication scheme.
        builder.Services.AddOptions<TenantResolutionOptions>()
            .Bind(configuration.GetSection(TenantResolutionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.TryAddScoped<ITenantContext, HttpTenantContext>();

        if (useRemoteAuthentication)
        {
            builder.Services.AddSamaAuthentication(configuration);
        }

        builder.Services.AddSamaAuthorization();

        // Replaces the fail-open default registered by AddSamaAuthentication now that a
        // Redis connection is available.
        builder.Services.AddSingleton<ITokenRevocationStore, RedisTokenRevocationStore>();
        builder.Services.AddSingleton<RedisTokenRevocationStore>();

        var cors = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
        builder.Services.AddCors(options => options.AddPolicy(CorsOptions.PolicyName, policy =>
        {
            if (cors.AllowedOrigins.Length == 0)
            {
                return;
            }

            policy.WithOrigins(cors.AllowedOrigins)
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                .AllowCredentials()
                .WithExposedHeaders("RateLimit-Limit", "RateLimit-Remaining", "RateLimit-Reset", "Retry-After", "ETag")
                .SetPreflightMaxAge(TimeSpan.FromHours(1));
        }));

        // --- Health ---------------------------------------------------------------------
        var health = builder.Services.AddHealthChecks();

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            health.AddRedis(redisConnection, name: "redis", tags: ["ready"]);
        }

        var postgres = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            health.AddNpgSql(postgres, name: "postgres", tags: ["ready"]);
        }

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = SamaJson.Lenient.PropertyNamingPolicy;
            foreach (var converter in SamaJson.Lenient.Converters)
            {
                options.SerializerOptions.Converters.Add(converter);
            }
        });

        builder.Services.AddOpenApi();

        return builder;
    }

    /// <summary>
    /// Installs the shared pipeline. The order below is load-bearing — see the comments;
    /// several of these steps are only correct in this position.
    /// </summary>
    public static WebApplication UseSamaServiceDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // First: everything downstream, including the error response itself, benefits from
        // being wrapped.
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // Before anything that reads the caller's address, so rate limiting and logs see the
        // real client rather than the load balancer.
        app.UseSamaClientIp();

        app.UseSamaSecurityHeaders();

        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) =>
                exception is not null ? Serilog.Events.LogEventLevel.Error
                : context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
                : IsProbePath(context.Request.Path) ? Serilog.Events.LogEventLevel.Verbose
                : Serilog.Events.LogEventLevel.Information;
        });

        app.UseRouting();
        app.UseCors(CorsOptions.PolicyName);

        app.UseAuthentication();
        app.UseAuthorization();

        // After authentication so user-partitioned buckets can see the principal, and after
        // routing so the endpoint's policy metadata is resolvable.
        app.UseSamaRateLimiting();

        return app;
    }

    /// <summary>Maps the probe endpoints. Call after the service maps its own routes.</summary>
    public static WebApplication MapSamaDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Liveness answers "is this process wedged", and deliberately checks nothing external.
        // A liveness probe that tested Redis would restart every pod in the fleet during a
        // Redis failover, turning a degraded cache into a full outage.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .DisableRateLimit();

        // Readiness answers "can this pod serve traffic", so it does test dependencies —
        // failing it removes the pod from the load balancer without killing it.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready"),
                ResponseWriter = WriteHealthResponseAsync,
            })
            .AllowAnonymous()
            .DisableRateLimit();

        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous();
        }

        return app;
    }

    private static bool IsProbePath(PathString path)
        => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,

                // The exception message can name hosts and credentials; the check name and
                // status are all a probe consumer needs.
                error = entry.Value.Exception is null ? null : "check failed",
            }),
        });
    }
}
