using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sama.Shared.Security.Authentication;
using Sama.Shared.Security.Authorization;

namespace Sama.Shared.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Wires bearer-token authentication for a resource service: signature and lifetime
    /// validation against Sama.Identity's published keys, plus the two checks the framework
    /// does not do for us — token-kind confusion and revocation.
    /// </summary>
    public static IServiceCollection AddSamaAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                  ?? throw new InvalidOperationException(
                      $"Configuration section '{JwtOptions.SectionName}' is missing. The service cannot validate tokens.");

        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.TryAddSingleton<ITokenRevocationStore, NullTokenRevocationStore>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = jwt.Authority;
                options.Audience = jwt.Audience;
                options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;

                // Keep the wire claim names ("sub", "perm", "tid"). Without this, the handler
                // helpfully rewrites them to long WS-Federation URIs and every lookup misses.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                    NameClaimType = SamaClaimTypes.Subject,
                    RoleClaimType = SamaClaimTypes.Role,

                    // Pin the algorithm. Without this a token could arrive signed with a
                    // symmetric algorithm whose "key" is the public key everyone can fetch.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                };

                options.Events = SamaJwtEvents.Create();
            });

        return services;
    }

    /// <summary>Registers the permission-based policy machinery.</summary>
    public static IServiceCollection AddSamaAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorization(options =>
        {
            // Endpoints opt in to anonymous access explicitly; forgetting an [Authorize]
            // should fail closed, not silently publish an endpoint.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        // Replace, not TryAdd: AddAuthorization() above has already registered
        // DefaultAuthorizationPolicyProvider, so a TryAdd here would do nothing and every
        // "perm:*" policy would resolve to null - surfacing as a 500 rather than a 403.
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>());
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    internal static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null)
        {
            context.Fail("No principal was produced.");
            return;
        }

        // A refresh token is signed by the same key as an access token. Without this check
        // a stolen refresh token would authenticate directly against every resource service.
        var tokenType = principal.FindFirst(SamaClaimTypes.TokenType)?.Value;
        if (!string.Equals(tokenType, SamaClaimTypes.AccessTokenType, StringComparison.Ordinal))
        {
            context.Fail("Only access tokens are accepted on resource endpoints.");
            return;
        }

        if (!Guid.TryParse(principal.FindFirst(SamaClaimTypes.Subject)?.Value, out var userId))
        {
            context.Fail("The token has no usable subject claim.");
            return;
        }

        var revocations = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationStore>();
        var cancellationToken = context.HttpContext.RequestAborted;

        if (Guid.TryParse(principal.FindFirst(SamaClaimTypes.TokenId)?.Value, out var tokenId)
            && await revocations.IsTokenRevokedAsync(tokenId, cancellationToken))
        {
            context.Fail("This token has been revoked.");
            return;
        }

        var stamp = principal.FindFirst(SamaClaimTypes.SecurityStamp)?.Value;
        if (!string.IsNullOrEmpty(stamp) && await revocations.IsStampStaleAsync(userId, stamp, cancellationToken))
        {
            context.Fail("The session is no longer valid; sign in again.");
        }
    }

    /// <summary>
    /// Replaces the default empty 401 with a problem+json body, so clients get the same
    /// error shape here as from every other failure path.
    /// </summary>
    internal static Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        if (context.Handled)
        {
            return Task.CompletedTask;
        }

        context.HandleResponse();
        return WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status401Unauthorized,
            "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.2",
            "Unauthorized",
            "A valid access token is required to call this endpoint.");
    }

    internal static Task OnForbiddenAsync(ForbiddenContext context)
        => WriteProblemAsync(
            context.HttpContext,
            StatusCodes.Status403Forbidden,
            "https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.4",
            "Forbidden",
            "Your account does not hold the permission required for this operation.");

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string type,
        string title,
        string detail)
    {
        if (context.Response.HasStarted)
        {
            context.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(SecurityServiceCollectionExtensions))
                .LogWarning("Cannot write an auth problem response; the response already started.");
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type,
            title,
            status,
            detail,
            traceId = context.TraceIdentifier,
        }));
    }
}

/// <summary>
/// The bearer-token event handlers shared by every Sama service.
/// <para>
/// Sama.Identity validates with locally-held keys while the resource services validate
/// against published ones, but both must apply the same two checks the framework does not:
/// reject refresh tokens presented as access tokens, and consult the revocation store.
/// Sharing one factory is what keeps those from drifting apart.
/// </para>
/// </summary>
public static class SamaJwtEvents
{
    public static JwtBearerEvents Create() => new()
    {
        OnTokenValidated = SecurityServiceCollectionExtensions.OnTokenValidatedAsync,
        OnChallenge = SecurityServiceCollectionExtensions.OnChallengeAsync,
        OnForbidden = SecurityServiceCollectionExtensions.OnForbiddenAsync,
    };
}
