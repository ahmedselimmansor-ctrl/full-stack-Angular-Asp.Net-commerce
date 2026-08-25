using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Sama.Shared.Security.Authentication;

namespace Sama.Shared.Infrastructure.RateLimiting;

/// <summary>Names the rate-limit policy that guards an endpoint.</summary>
public sealed class RateLimitMetadata(string policyName)
{
    public string PolicyName { get; } = policyName;
}

/// <summary>Opts an endpoint out of rate limiting entirely (health and readiness probes).</summary>
public sealed class DisableRateLimitMetadata;

public static class RateLimitEndpointExtensions
{
    /// <summary>Applies a named policy: <c>app.MapGet("/products", …).RateLimit("read")</c>.</summary>
    public static TBuilder RateLimit<TBuilder>(this TBuilder builder, string policyName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(b => b.Metadata.Add(new RateLimitMetadata(policyName)));
        return builder;
    }

    public static TBuilder DisableRateLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(b => b.Metadata.Add(new DisableRateLimitMetadata()));
        return builder;
    }
}

/// <summary>
/// Applies the endpoint's rate-limit policy and annotates every response with RateLimit-*
/// headers, so a well-behaved client can back off before it gets rejected.
/// </summary>
public sealed class RateLimitMiddleware(
    RequestDelegate next,
    IRateLimiter limiter,
    IOptions<RateLimitOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<DisableRateLimitMetadata>() is not null)
        {
            await next(context);
            return;
        }

        var policyName = endpoint?.Metadata.GetMetadata<RateLimitMetadata>()?.PolicyName;
        if (policyName is null)
        {
            await next(context);
            return;
        }

        if (!options.Value.Policies.TryGetValue(policyName, out var policy))
        {
            throw new InvalidOperationException($"Rate-limit policy '{policyName}' is not configured.");
        }

        var partitionKey = ResolvePartitionKey(context, policy.Partition);
        var decision = await limiter.AcquireAsync(policyName, partitionKey, context.RequestAborted);

        WriteRateLimitHeaders(context, decision);

        if (decision.IsAllowed)
        {
            await next(context);
            return;
        }

        await WriteTooManyRequestsAsync(context, decision);
    }

    /// <summary>
    /// Builds the bucket key. Note this reads <see cref="ConnectionInfo.RemoteIpAddress"/>,
    /// which is only the true client IP once ForwardedHeaders has run against a trusted proxy
    /// list — otherwise a caller could spoof X-Forwarded-For and get a fresh bucket per request.
    /// </summary>
    internal static string ResolvePartitionKey(HttpContext context, RateLimitPartition partition)
    {
        switch (partition)
        {
            case RateLimitPartition.ApiKey:
                var apiKey = context.Request.Headers["X-Api-Key"].ToString();
                return string.IsNullOrEmpty(apiKey)
                    ? "ip:" + ClientIp(context)
                    : "key:" + Fingerprint(apiKey);

            case RateLimitPartition.IpAddress:
                return "ip:" + ClientIp(context);

            case RateLimitPartition.IpAndSubject:
                var subject = context.User.FindFirst(SamaClaimTypes.Subject)?.Value ?? "anon";
                return $"ip:{ClientIp(context)}|s:{subject}";

            case RateLimitPartition.User:
            default:
                var userId = context.User.FindFirst(SamaClaimTypes.Subject)?.Value;
                return string.IsNullOrEmpty(userId)
                    ? "ip:" + ClientIp(context)
                    : "u:" + userId;
        }
    }

    private static string ClientIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return "unknown";
        }

        // Collapse IPv6 clients to their /64. A single residential IPv6 allocation hands out
        // billions of addresses, so limiting per-address would be no limit at all.
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes).ToString() + "/64";
        }

        return address.ToString();
    }

    /// <summary>Hashes the API key so the raw secret never lands in a Redis key or a log line.</summary>
    private static string Fingerprint(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];

    private static void WriteRateLimitHeaders(HttpContext context, RateLimitDecision decision)
    {
        var headers = context.Response.Headers;
        headers["RateLimit-Limit"] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Remaining"] = decision.Remaining.ToString(CultureInfo.InvariantCulture);
        headers["RateLimit-Reset"] =
            ((int)Math.Ceiling(decision.ResetAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    }

    private static async Task WriteTooManyRequestsAsync(HttpContext context, RateLimitDecision decision)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://datatracker.ietf.org/doc/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "You have sent too many requests. Retry after the interval in the Retry-After header.",
            traceId = context.TraceIdentifier,
        }));
    }
}

public static class RateLimitApplicationBuilderExtensions
{
    /// <summary>
    /// Must run after routing (it reads endpoint metadata) and after authentication
    /// (user-partitioned buckets need the resolved principal).
    /// </summary>
    public static IApplicationBuilder UseSamaRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RateLimitMiddleware>();
    }
}
