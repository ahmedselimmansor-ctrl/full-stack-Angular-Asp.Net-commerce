using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Sama.Identity.Features.Auth;

/// <summary>
/// Issues the refresh token as an HttpOnly cookie alongside the JSON response.
/// <para>
/// A browser SPA has nowhere safe to put a long-lived refresh token: <c>localStorage</c> and
/// <c>sessionStorage</c> are both readable by any script that gets injected, so a single XSS
/// yields a credential that renews itself indefinitely. An HttpOnly cookie is not readable
/// from JavaScript at all, which bounds an XSS to whatever it can do while the page is open
/// rather than handing over durable account access.
/// </para>
/// <para>
/// Native clients cannot use cookies as conveniently, so the token is <i>also</i> returned in
/// the body. The web client ignores that field and relies on the cookie.
/// </para>
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "sama_rt";

    /// <summary>
    /// Scoped to the auth routes. A cookie sent on every request to every path would ride
    /// along on catalogue and basket calls that have no business seeing it.
    /// </summary>
    private const string CookiePath = "/api/v1/auth";

    public static void Issue(HttpContext context, string refreshToken, int lifetimeDays, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(environment);

        context.Response.Cookies.Append(Name, refreshToken, new CookieOptions
        {
            HttpOnly = true,

            // Secure is relaxed only for plain-HTTP local development; over a real origin the
            // cookie must never be sent in the clear.
            Secure = !environment.IsDevelopment(),

            // Strict, not Lax: this cookie's only job is to authorise a token refresh, and no
            // legitimate flow initiates that from another site. Strict removes the CSRF vector
            // for the one endpoint that accepts it without a bearer token.
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            MaxAge = TimeSpan.FromDays(lifetimeDays),
            IsEssential = true,
        });
    }

    /// <summary>Reads the cookie, for clients that do not send the token in the body.</summary>
    public static string? Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    public static void Clear(HttpContext context, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(environment);

        // Attributes must match those used to set it, or the browser creates a second cookie
        // instead of removing the original.
        context.Response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        });
    }
}
