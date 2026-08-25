using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Sama.Shared.Infrastructure.RateLimiting;

namespace Sama.Identity.Features.Keys;

/// <summary>
/// Publishes the metadata every other service needs to validate a Sama token.
/// <para>
/// Serving a discovery document rather than hard-coding public keys into each service is what
/// makes key rotation a configuration change in one place: the bearer handler's
/// ConfigurationManager re-fetches on a cadence and on encountering an unknown <c>kid</c>,
/// so a new signing key propagates without redeploying six services.
/// </para>
/// </summary>
public static class DiscoveryEndpoints
{
    // Static: the document is rebuilt on every metadata fetch, and these values never vary.
    private static readonly string[] SigningAlgorithms = ["RS256"];
    private static readonly string[] ResponseTypes = ["token"];
    private static readonly string[] SubjectTypes = ["public"];
    private static readonly string[] GrantTypes = ["password", "refresh_token"];
    private static readonly string[] SupportedClaims =
        ["sub", "tid", "email", "name", "role", "perm", "jti", "typ", "sst"];

    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet("/.well-known/openid-configuration", (IOptions<SigningOptions> options, HttpContext context) =>
            {
                var issuer = options.Value.Issuer.TrimEnd('/');

                context.Response.Headers.CacheControl = "public, max-age=300";

                return Results.Ok(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["issuer"] = issuer,
                    ["jwks_uri"] = $"{issuer}/.well-known/jwks.json",
                    ["token_endpoint"] = $"{issuer}/api/v1/auth/login",
                    ["id_token_signing_alg_values_supported"] = SigningAlgorithms,
                    ["response_types_supported"] = ResponseTypes,
                    ["subject_types_supported"] = SubjectTypes,
                    ["grant_types_supported"] = GrantTypes,
                    ["claims_supported"] = SupportedClaims,
                });
            })
            .AllowAnonymous()
            .DisableRateLimit()
            .WithTags("Discovery")
            .WithSummary("OpenID-style metadata describing this issuer.");

        routes.MapGet("/.well-known/jwks.json", (SigningKeyProvider keys, HttpContext context) =>
            {
                // Cacheable, but not for long: during a rotation an out-of-date JWKS is the
                // difference between a validator accepting new tokens and rejecting all of them.
                context.Response.Headers.CacheControl = "public, max-age=300";

                var keySet = keys.BuildJsonWebKeySet();

                return Results.Ok(new
                {
                    keys = keySet.Keys.Select(key => new
                    {
                        kty = key.Kty,
                        use = key.Use,
                        alg = key.Alg,
                        kid = key.Kid,
                        n = key.N,
                        e = key.E,
                    }),
                });
            })
            .AllowAnonymous()
            .DisableRateLimit()
            .WithTags("Discovery")
            .WithSummary("Public signing keys, for token validation.");

        return routes;
    }
}
