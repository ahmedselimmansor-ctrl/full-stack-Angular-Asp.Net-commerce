using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Sama.Shared.Kernel;

namespace Sama.Shared.Infrastructure.Http;

/// <summary>
/// Response-caching helpers. These control Cloud CDN as much as the browser: the CDN honours
/// <c>s-maxage</c> and <c>stale-while-revalidate</c>, which is what lets a catalogue page be
/// served from the edge while a single origin request refreshes it in the background.
/// </summary>
public static class HttpCaching
{
    /// <summary>
    /// Marks a response cacheable by shared caches.
    /// <para>
    /// Only ever call this for data that is identical for every caller of this URL. Cloud CDN
    /// keys on host + path + query (and skips caching entirely when an <c>Authorization</c>
    /// header is present), so per-user data must not travel this path.
    /// </para>
    /// </summary>
    public static void SetPublicCache(
        this HttpResponse response,
        TimeSpan browserMaxAge,
        TimeSpan sharedMaxAge,
        TimeSpan? staleWhileRevalidate = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        var stale = staleWhileRevalidate ?? TimeSpan.FromSeconds(sharedMaxAge.TotalSeconds * 2);

        response.Headers.CacheControl = string.Create(
            CultureInfo.InvariantCulture,
            $"public, max-age={(int)browserMaxAge.TotalSeconds}, s-maxage={(int)sharedMaxAge.TotalSeconds}, stale-while-revalidate={(int)stale.TotalSeconds}");

        // Without this a cache can serve a gzip body to a client that cannot decode it.
        response.Headers.Vary = HeaderNames.AcceptEncoding;
    }

    /// <summary>Forbids storage anywhere. For baskets, orders, tokens and anything user-specific.</summary>
    public static void SetNoStore(this HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        response.Headers.Pragma = "no-cache";
    }

    /// <summary>
    /// Applies an ETag and, when the client already holds it, completes the request as a 304.
    /// <para>
    /// Worth doing even behind a CDN: on a cache miss the edge revalidates against the origin,
    /// and a 304 saves serialising and shipping the body over that hop as well.
    /// </para>
    /// </summary>
    public static bool TryCompleteWithETag<T>(this HttpContext context, T payload)
    {
        ArgumentNullException.ThrowIfNull(context);

        var etag = ComputeETag(payload);
        context.Response.Headers.ETag = etag;

        var ifNoneMatch = context.Request.Headers.IfNoneMatch;
        if (ifNoneMatch.Count == 0)
        {
            return false;
        }

        foreach (var candidate in ifNoneMatch)
        {
            // A CDN may weaken the tag on the way through, so compare ignoring the W/ prefix.
            if (candidate is not null && Matches(candidate, etag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;

                // RFC 9110: a 304 carries no body, and Content-Length must not contradict that.
                context.Response.ContentLength = null;
                return true;
            }
        }

        return false;
    }

    private static bool Matches(string candidate, string etag)
    {
        foreach (var part in candidate.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "*" || Normalise(part) == Normalise(etag))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalise(string tag)
        => tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;

    /// <summary>
    /// Weak ETag over the serialised payload. Weak because it asserts semantic equivalence,
    /// not byte equality — which is all a client revalidating a JSON document needs.
    /// </summary>
    public static string ComputeETag<T>(T payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, SamaJson.Default);
        var hash = SHA256.HashData(json);

        return string.Create(CultureInfo.InvariantCulture, $"W/\"{Convert.ToHexStringLower(hash)[..32]}\"");
    }

    /// <summary>
    /// Emits the cache tags Cloud CDN purges by, so a price change can evict exactly the
    /// affected entries instead of the whole cache.
    /// </summary>
    public static void SetCacheTags(this HttpResponse response, params ReadOnlySpan<string> tags)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (tags.Length == 0)
        {
            return;
        }

        var builder = new StringBuilder();
        foreach (var tag in tags)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(tag);
        }

        response.Headers["Cache-Tag"] = builder.ToString();
    }
}
