using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sama.Shared.Security.Authentication;

/// <summary>
/// Response hardening headers. The API serves JSON, not documents, so the policy is the
/// restrictive one: deny framing outright and forbid every script/style source. The
/// storefront's own (much looser) CSP is served by the Angular host, not from here.
/// </summary>
public static class SecurityHeadersMiddleware
{
    public static IApplicationBuilder UseSamaSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(static async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Stop a browser second-guessing our Content-Type and executing a JSON body as script.
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), payment=(), usb=()";

            // HSTS is emitted by the load balancer for the public origin; repeating it here
            // covers direct in-cluster HTTPS callers without conflicting.
            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
            }

            // Server/technology fingerprints give an attacker a free version-match on CVEs.
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            await next(context);
        });
    }
}
